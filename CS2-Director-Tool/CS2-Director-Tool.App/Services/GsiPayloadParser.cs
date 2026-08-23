using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace CS2_Director_Tool.App.Services
{
    /// <summary>
    /// 解析器：将 CS2 游戏状态集成（GSI）通过 HTTP 上传的 JSON 负载转换为 <see cref="JObject"/>。
    /// 处理截断、残缺或包含多个连续 JSON 对象的负载（GSI 可能批量发送）。
    /// </summary>
    public static class GsiPayloadParser
    {
        /// <summary>
        /// 解析 GSI 负载的结果。
        /// </summary>
        public sealed class GsiParseResult
        {
            /// <summary>
            /// 获取或设置解析出的数据对象。若解析失败则为 <c>null</c>。
            /// </summary>
            public JObject Data { get; set; }

            /// <summary>
            /// 获取或设置指示负载是否已完整解析的值。
            /// </summary>
            public bool IsFullyParsed { get; set; }

            /// <summary>
            /// 获取或设置指示是否从损坏负载中恢复了数据的值。
            /// </summary>
            public bool IsRecovered { get; set; }

            /// <summary>
            /// 获取或设置解析失败时包含的错误消息（若有）。
            /// </summary>
            public string ErrorMessage { get; set; }

            /// <summary>
            /// 获取或设置在恢复场景下提供的调试上下文片段（若有）。
            /// </summary>
            public string ContextSnippet { get; set; }
        }

        /// <summary>
        /// 解析 GSI 负载。
        /// </summary>
        /// <param name="payload">从 GSI 接收到的原始负载字符串。</param>
        /// <returns>包含解析出的对象、状态标志与错误信息的 <see cref="GsiParseResult"/>。</returns>
        public static GsiParseResult Parse(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return new GsiParseResult { ErrorMessage = "空负载" };

            // 优先获取第一个完整 JSON 对象。GSI 有时会在单一负载中连续发送多个对象，
            // 因此我们需要隔离第一个封装对象，避免 JObject.Parse 失败。
            string firstObject = TryExtractBalancedObject(payload);

            if (firstObject != null)
            {
                try
                {
                    var data = JObject.Parse(firstObject);
                    return new GsiParseResult
                    {
                        Data = data,
                        IsFullyParsed = true,
                        IsRecovered = false
                    };
                }
                catch (Exception ex)
                {
                    // JObject.Parse 可能由于截断的 UTF-8 多字节字符或内联注释而失败。
                    // 尝试尽力恢复，仅提取 map 与 round 字段。
                    var recovered = TryRecoverMapAndRound(firstObject, out string snippet);
                    if (recovered != null)
                    {
                        return new GsiParseResult
                        {
                            Data = recovered,
                            IsFullyParsed = false,
                            IsRecovered = true,
                            ErrorMessage = $"已尽力恢复：{ex.Message}",
                            ContextSnippet = snippet
                        };
                    }

                    return new GsiParseResult
                    {
                        Data = null,
                        IsFullyParsed = false,
                        IsRecovered = false,
                        ErrorMessage = $"JSON 解析失败：{ex.Message}",
                        ContextSnippet = BuildContextSnippet(payload, 0, 200)
                    };
                }
            }

            // 回退：尝试整体解析（处理单对象且无前导垃圾的情况）。
            try
            {
                var data = JObject.Parse(payload);
                return new GsiParseResult
                {
                    Data = data,
                    IsFullyParsed = true,
                    IsRecovered = false
                };
            }
            catch (Exception ex)
            {
                var recovered = TryRecoverMapAndRound(payload, out string snippet);
                if (recovered != null)
                {
                    return new GsiParseResult
                    {
                        Data = recovered,
                        IsFullyParsed = false,
                        IsRecovered = true,
                        ErrorMessage = $"已尽力恢复：{ex.Message}",
                        ContextSnippet = snippet
                    };
                }

                return new GsiParseResult
                {
                    Data = null,
                    IsFullyParsed = false,
                    IsRecovered = false,
                    ErrorMessage = $"JSON 解析失败：{ex.Message}",
                    ContextSnippet = BuildContextSnippet(payload, 0, 200)
                };
            }
        }

        /// <summary>
        /// 在可能包含多个连续 JSON 对象的字符串中，提取第一个平衡（完整）的 JSON 对象。
        /// </summary>
        /// <param name="payload">可能包含多个 JSON 对象的原始负载。</param>
        /// <returns>第一个完整 JSON 对象字符串，若找不到则为 <c>null</c>。</returns>
        private static string TryExtractBalancedObject(string payload)
        {
            int startIndex = payload.IndexOf('{');
            if (startIndex < 0)
                return null;

            int depth = 0;
            bool inString = false;
            bool escape = false;

            for (int i = startIndex; i < payload.Length; i++)
            {
                char c = payload[i];

                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                    continue;

                if (c == '{')
                    depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        // 包含从 startIndex 到 i（含）的字符。
                        return payload.Substring(startIndex, i - startIndex + 1);
                    }
                }
            }

            // 未找到平衡对象：返回从第一个 '{' 开始的剩余负载（截断场景）。
            return payload.Substring(startIndex);
        }

        /// <summary>
        /// 从损坏的负载中尽力提取 map 与 round 字段。
        /// </summary>
        /// <param name="payload">原始或部分 JSON 负载。</param>
        /// <param name="snippet">输出上下文片段（若有）。</param>
        /// <returns>包含 map 与 round 的 <see cref="JObject"/>，若无法恢复则为 <c>null</c>。</returns>
        private static JObject TryRecoverMapAndRound(string payload, out string snippet)
        {
            snippet = null;
            try
            {
                // 查找 "map" 与 "round" 关键字并提取其后的对象/字符串。
                int mapIdx = payload.IndexOf("\"map\"", StringComparison.Ordinal);
                int roundIdx = payload.IndexOf("\"round\"", StringComparison.Ordinal);

                if (mapIdx < 0 || roundIdx < 0)
                    return null;

                string mapPart = payload.Substring(mapIdx);
                int mapObjStart = mapPart.IndexOf('{');
                if (mapObjStart < 0)
                    return null;

                // 提取子对象。
                string mapSub = TryExtractBalancedObject(mapPart.Substring(mapObjStart));
                if (mapSub == null)
                    return null;

                var mapObject = JObject.Parse(mapSub);

                int roundObjStart = payload.Substring(roundIdx).IndexOf('{');
                if (roundObjStart < 0)
                    return null;

                string roundSub = TryExtractBalancedObject(payload.Substring(roundIdx + roundObjStart));
                if (roundSub == null)
                    return null;

                var roundObject = JObject.Parse(roundSub);

                var result = new JObject();
                result["map"] = mapObject;
                result["round"] = roundObject;

                snippet = BuildContextSnippet(payload, Math.Min(mapIdx, roundIdx), 200);
                return result;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 为调试构建上下文片段。
        /// </summary>
        private static string BuildContextSnippet(string payload, int start, int length)
        {
            if (start < 0 || start >= payload.Length)
                return payload.Length <= length ? payload : payload.Substring(0, length);

            int actualStart = Math.Max(0, start);
            int actualLen = Math.Min(length, payload.Length - actualStart);
            string raw = payload.Substring(actualStart, actualLen);

            // 为每行添加行号，便于定位。
            var sb = new StringBuilder();
            using (var reader = new StringReader(raw))
            {
                string line;
                int lineNumber = 1;
                while ((line = reader.ReadLine()) != null)
                {
                    sb.AppendLine($"{lineNumber}: {line}");
                    lineNumber++;
                }
            }

            return sb.ToString();
        }
    }
}
