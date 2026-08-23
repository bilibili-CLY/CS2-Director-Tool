using System.ComponentModel;

namespace CS2_Director_Tool.App.Models
{
    /// <summary>
    /// 玩家条目：表示由其 Steam ID 标识的已观察玩家，以及解析出的替换名称所需的状态。
    /// </summary>
    public class PlayerEntry : INotifyPropertyChanged
    {
        private string _steamId = string.Empty;
        private string _name = string.Empty;
        private bool _isResolved;
        private bool _isLoading;
        private bool _hasError;
        private string _registeredName = string.Empty;
        private string _resolveNote = string.Empty;

        /// <summary>获取或设置 Steam 64 位 ID。</summary>
        public string SteamId
        {
            get => _steamId;
            set
            {
                if (_steamId != value)
                {
                    _steamId = value;
                    OnPropertyChanged(nameof(SteamId));
                }
            }
        }

        /// <summary>获取或设置来自 GSI 的观察到的玩家名称。</summary>
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }

        /// <summary>获取或设置一个值，指示替换名称是否已成功解析。</summary>
        public bool IsResolved
        {
            get => _isResolved;
            set
            {
                if (_isResolved != value)
                {
                    _isResolved = value;
                    OnPropertyChanged(nameof(IsResolved));
                }
            }
        }

        /// <summary>获取或设置一个值，指示当前是否正在解析名称。</summary>
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged(nameof(IsLoading));
                }
            }
        }

        /// <summary>获取或设置一个值，指示解析过程中是否发生错误（例如未找到）。</summary>
        public bool HasError
        {
            get => _hasError;
            set
            {
                if (_hasError != value)
                {
                    _hasError = value;
                    OnPropertyChanged(nameof(HasError));
                }
            }
        }

        /// <summary>获取或设置从 Majo Cup 网站解析出的登记名称。</summary>
        public string RegisteredName
        {
            get => _registeredName;
            set
            {
                if (_registeredName != value)
                {
                    _registeredName = value;
                    OnPropertyChanged(nameof(RegisteredName));
                }
            }
        }

        /// <summary>获取或设置解析状态的简短说明（例如“已解析”、“未找到”、错误信息）。</summary>
        public string ResolveNote
        {
            get => _resolveNote;
            set
            {
                if (_resolveNote != value)
                {
                    _resolveNote = value;
                    OnPropertyChanged(nameof(ResolveNote));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
