# 对阵双方名单接口调用文档

按对阵图内的**对局 ID（gameId）**查询对阵双方战队与队员的名单接口。基础地址：`/api`（请求头见「通用约定」）。

## 通用约定

- 响应统一格式：`{ "success": bool, "code": string, "message": string, "data": T }`。
- 本接口 **免登录**（`AllowAnonymous`）。
- 编码一律 UTF-8；请求/响应均为 JSON。
- 对局 ID 来自对阵图/赛程接口（`GET /api/matches/{matchId}/bracket`、`GET /api/matches/{matchId}/schedule`）返回的 `id` 字段。

## 1. 查询对阵双方名单

| 项 | 值 |
|----|----|
| 方法 | `GET` |
| 路径 | `/api/matches/games/{gameId}/roster` |
| 鉴权 | 无 |

- `gameId`：对阵图内的对局 ID（Guid）。

请求示例：

```bash
curl "http://localhost:5080/api/matches/games/3d1a1c86-9c38-4d1e-8e2f-5e5f8456a1c0/roster"
```

**响应：`data` 为对阵双方名单**

| 字段 | 类型 | 说明 |
|------|------|------|
| gameId | string (Guid) | 对局 ID |
| teamA / teamB | object \| null | 一侧战队信息；`BYE` 轮空或后续轮次「待定」时为 `null` |

`teamA` / `teamB` 对象字段：

| 字段 | 类型 | 说明 |
|------|------|------|
| teamId | string (Guid) | 战队内部 ID |
| perfectId | string? | 完美平台战队 ID（对外路由 / 页面跳转用） |
| teamName | string | 战队名 |
| logoUrl | string? | 战队 LOGO（相对路径，如 `/uploads/xxx.png`） |
| members | array | 队员列表（队长排最前，其余按首发→替补→教练） |

`members[]` 项字段：

| 字段 | 类型 | 说明 |
|------|------|------|
| userId | string (Guid) | 用户内部 ID |
| uid | int? | 对外用户身份码（绑定账号时才有，未绑定为 null） |
| username | string | 注册用户名 |
| nickname | string? | 对外展示昵称（来自 `player_profiles.Name`） |
| avatarUrl | string? | 玩家头像（相对路径，如 `/uploads/avatars/xxx.webp`） |
| steamId | string? | 玩家绑定的 Steam64 ID（未绑定为空） |
| role | int | 身份：0=首发，1=替补，2=教练 |
| isCaptain | bool | 是否队长 |

响应示例：

```json
{
  "success": true,
  "code": "OK",
  "message": "ok",
  "data": {
    "gameId": "3d1a1c86-9c38-4d1e-8e2f-5e5f8456a1c0",
    "teamA": {
      "teamId": "b2d39f70-...",
      "perfectId": "100861",
      "teamName": "鱼籽战队",
      "logoUrl": "/uploads/logos/abc.png",
      "members": [
        {
          "userId": "5c0f...",
          "uid": 12,
          "username": "juliett",
          "nickname": "Juliett",
          "avatarUrl": "/uploads/avatars/x.webp",
          "steamId": "76561198250000000",
          "role": 0,
          "isCaptain": true
        },
        { "userId": "7a11...", "uid": 18, "username": "jack", "nickname": "Jack", "avatarUrl": null, "steamId": null, "role": 1, "isCaptain": false }
      ]
    },
    "teamB": null
  }
}
```

> 说明：
> - 一侧为 `null` 表示该位置尚未落定（首轮 `BYE` 轮空，或后续轮次胜者「待定」）。
> - `nickname` 来自 `player_profiles` 表（demo 解析后更新），与 `username` 是两套字段；`nickname` 才是对外展示昵称。
> - `avatarUrl`、`steamId` 可能为空（玩家未上传头像 / 未绑定 Steam），前端需做占位兜底。

**错误**

| 场景 | HTTP | code | message |
|------|------|------|---------|
| gameId 不是合法 Guid | 404 | NOT_FOUND | 对局不存在 |
| 对局不存在（含草稿比赛占位） | 404 | NOT_FOUND | 对局不存在 |

## 关联接口

我的页面先取对阵图，再按对局 ID 取名单：

```bash
# 1. 取对阵图（挑一场对局，拿到 gameId）
curl "http://localhost:5080/api/matches/{matchId}/bracket"

# 2. 取该场双方名单
curl "http://localhost:5080/api/matches/games/{gameId}/roster"
```