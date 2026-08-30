using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CS2_Director_Tool.App.Models;
using CS2_Director_Tool.App.Services;

namespace CS2_Director_Tool.App.ViewModels
{
    /// <summary>
    /// 事件动作页面视图模型：配置 GSI 事件触发后要执行的动作，
    /// 并支持将规则集保存为命名预设、一键加载。
    /// </summary>
    public partial class EventActionViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly IEventActionService _eventActionService;
        private readonly ILogService _log;
        private bool _isEnabled;
        private string _status = string.Empty;
        private bool _hasRules;
        private EventActionPreset? _selectedPreset;
        private string _newPresetName = string.Empty;

        /// <summary>当前配置的事件动作规则列表。</summary>
        public ObservableCollection<EventActionRule> Rules { get; } = new();

        /// <summary>已保存的事件动作预设列表。</summary>
        public ObservableCollection<EventActionPreset> Presets { get; } = new();

        /// <summary>可选的事件类型集合，供下拉选择。</summary>
        public IReadOnlyList<GsiEventType> EventTypes { get; }

        /// <summary>可选的待执行动作类型集合，供下拉选择。</summary>
        public IReadOnlyList<EventActionType> ActionTypes { get; }

        /// <summary>添加规则的命令。</summary>
        public IRelayCommand AddRuleCommand { get; }

        /// <summary>删除指定规则的命令。</summary>
        public IRelayCommand<EventActionRule> RemoveRuleCommand { get; }

        /// <summary>为指定规则添加动作的命令。</summary>
        public IRelayCommand<EventActionRule> AddActionCommand { get; }

        /// <summary>从指定规则中删除动作的命令。</summary>
        public IRelayCommand<EventActionItem> RemoveActionCommand { get; }

        /// <summary>将当前规则保存为预设的命令。</summary>
        public IRelayCommand SavePresetCommand { get; }

        /// <summary>加载所选预设并整组应用为当前规则的命令。</summary>
        public IRelayCommand LoadPresetCommand { get; }

        /// <summary>删除所选预设的命令。</summary>
        public IRelayCommand DeletePresetCommand { get; }

        /// <summary>获取或设置当前选中的预设。</summary>
        public EventActionPreset? SelectedPreset
        {
            get => _selectedPreset;
            set => SetProperty(ref _selectedPreset, value);
        }

        /// <summary>获取或设置保存预设时使用的名称。</summary>
        public string NewPresetName
        {
            get => _newPresetName;
            set => SetProperty(ref _newPresetName, value);
        }

        /// <summary>获取或设置是否启用整个事件动作功能。</summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (SetProperty(ref _isEnabled, value))
                {
                    _settingsService.EventActionEnabled = value;
                    _log.Log(LogCategory.EventAction, value ? "已启用事件动作" : "已取消启用事件动作");
                    if (value)
                        RefreshStatus();
                }
            }
        }

        /// <summary>获取或设置页面上显示的当前状态文本。</summary>
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        /// <summary>获取或设置是否已存在规则（控制空状态提示的显示）。</summary>
        public bool HasRules
        {
            get => _hasRules;
            set
            {
                if (SetProperty(ref _hasRules, value))
                    OnPropertyChanged(nameof(HasNoRules));
            }
        }

        /// <summary>获取或设置是否尚无规则（与 <see cref="HasRules"/> 相反）。</summary>
        public bool HasNoRules => !HasRules;

        /// <summary>初始化 <see cref="EventActionViewModel"/> 类的新实例。</summary>
        public EventActionViewModel(ISettingsService settingsService, IEventActionService eventActionService,
            ILogService log)
        {
            _settingsService = settingsService;
            _eventActionService = eventActionService;
            _log = log;

            EventTypes = Enum.GetValues<GsiEventType>();
            ActionTypes = Enum.GetValues<EventActionType>();

            AddRuleCommand = new RelayCommand(AddRule);
            RemoveRuleCommand = new RelayCommand<EventActionRule>(RemoveRule);
            AddActionCommand = new RelayCommand<EventActionRule>(AddAction);
            RemoveActionCommand = new RelayCommand<EventActionItem>(RemoveAction);
            SavePresetCommand = new RelayCommand(SavePreset);
            LoadPresetCommand = new RelayCommand(LoadPreset);
            DeletePresetCommand = new RelayCommand(DeletePreset);

            _isEnabled = settingsService.EventActionEnabled;

            foreach (var rule in settingsService.EventActionRules)
            {
                Rules.Add(rule);
                SubscribeRule(rule);
            }

            foreach (var preset in settingsService.EventActionPresets)
                Presets.Add(preset);

            _eventActionService.SetRuleSource(() => IsEnabled, () => Rules.ToList());

            if (!IsEnabled)
            {
                Status = "事件动作功能未启用。";
            }
            else
            {
                RefreshStatus();
            }
        }

        private void RefreshStatus()
        {
            HasRules = Rules.Count > 0;
            Status = Rules.Count > 0
                ? $"已配置 {Rules.Count} 条规则，共 {Rules.Sum(r => r.Actions.Count)} 个动作。"
                : "尚未配置任何规则。可点击「添加规则」开始。";
        }

        private void AddRule()
        {
            var rule = new EventActionRule
            {
                EventType = GsiEventType.PauseStarted,
                IsEnabled = true
            };
            rule.Actions.Add(new EventActionItem { Type = EventActionType.PlayMedia, Target = string.Empty });
            Rules.Add(rule);
            SubscribeRule(rule);
            SaveRules();
            _log.Log(LogCategory.EventAction, "已添加新规则");
            RefreshStatus();
        }

        private void RemoveRule(EventActionRule? rule)
        {
            if (rule is null || !Rules.Remove(rule))
                return;
            UnsubscribeRule(rule);
            SaveRules();
            _log.Log(LogCategory.EventAction, $"已删除规则「{rule.EventTypeLabel}」");
            RefreshStatus();
        }

        private void AddAction(EventActionRule? rule)
        {
            if (rule is null)
                return;
            rule.Actions.Add(new EventActionItem { Type = EventActionType.PlayMedia, Target = string.Empty });
            SaveRules();
            RefreshStatus();
        }

        private void RemoveAction(EventActionItem? action)
        {
            if (action is null)
                return;
            var rule = Rules.FirstOrDefault(r => r.Actions.Contains(action));
            if (rule is null)
                return;
            rule.Actions.Remove(action);
            SaveRules();
            RefreshStatus();
        }

        private void SavePreset()
        {
            string name = string.IsNullOrWhiteSpace(NewPresetName)
                ? (SelectedPreset?.Name ?? string.Empty)
                : NewPresetName.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                _log.Log(LogCategory.EventAction, "保存预设失败：未指定预设名称");
                return;
            }

            var existing = Presets.FirstOrDefault(p => p.Name == name);
            if (existing is null)
            {
                existing = new EventActionPreset { Name = name };
                Presets.Add(existing);
                SelectedPreset = existing;
            }
            existing.Rules = CloneRules(Rules);
            SavePresets();
            _log.Log(LogCategory.EventAction, $"预设「{name}」已保存");
            NewPresetName = string.Empty;
        }

        private void LoadPreset()
        {
            var preset = SelectedPreset;
            if (preset is null)
            {
                _log.Log(LogCategory.EventAction, "加载预设失败：未选择任何预设");
                return;
            }
            ReplaceRules(preset.Rules);
            _log.Log(LogCategory.EventAction, $"已加载并应用预设「{preset.Name}」");
        }

        private void DeletePreset()
        {
            var preset = SelectedPreset;
            if (preset is null)
            {
                _log.Log(LogCategory.EventAction, "删除预设失败：未选择任何预设");
                return;
            }
            Presets.Remove(preset);
            SelectedPreset = null;
            SavePresets();
            _log.Log(LogCategory.EventAction, $"已删除预设「{preset.Name}」");
        }

        private void ReplaceRules(IEnumerable<EventActionRule> rules)
        {
            ClearRules();
            foreach (var rule in CloneRules(rules))
            {
                Rules.Add(rule);
                SubscribeRule(rule);
            }
            SaveRules();
            RefreshStatus();
        }

        private void ClearRules()
        {
            foreach (var rule in Rules)
                UnsubscribeRule(rule);
            Rules.Clear();
        }

        private static List<EventActionRule> CloneRules(IEnumerable<EventActionRule> rules)
        {
            var result = new List<EventActionRule>();
            foreach (var rule in rules)
            {
                var clone = new EventActionRule
                {
                    IsEnabled = rule.IsEnabled,
                    EventType = rule.EventType
                };
                foreach (var action in rule.Actions)
                    clone.Actions.Add(new EventActionItem { Type = action.Type, Target = action.Target });
                result.Add(clone);
            }
            return result;
        }

        private void SaveRules()
        {
            _settingsService.EventActionRules = Rules.ToList();
        }

        private void SavePresets()
        {
            _settingsService.EventActionPresets = Presets.ToList();
        }

        private void SubscribeRule(EventActionRule rule)
        {
            rule.PropertyChanged += OnRulePropertyChanged;
            rule.Actions.CollectionChanged += OnActionsChanged;
            foreach (var action in rule.Actions)
                action.PropertyChanged += OnActionPropertyChanged;
        }

        private void UnsubscribeRule(EventActionRule rule)
        {
            rule.PropertyChanged -= OnRulePropertyChanged;
            rule.Actions.CollectionChanged -= OnActionsChanged;
            foreach (var action in rule.Actions)
                action.PropertyChanged -= OnActionPropertyChanged;
        }

        private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            SaveRules();
        }

        private void OnActionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            SaveRules();
        }

        private void OnActionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems is not null)
            {
                foreach (var item in e.OldItems)
                    if (item is EventActionItem action)
                        action.PropertyChanged -= OnActionPropertyChanged;
            }

            if (e.NewItems is not null)
            {
                foreach (var item in e.NewItems)
                    if (item is EventActionItem action)
                        action.PropertyChanged += OnActionPropertyChanged;
            }

            SaveRules();
        }
    }
}
