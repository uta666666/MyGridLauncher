﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using AppTray.Commons;
using System.Drawing;
using AppTray.Models;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using Livet.Messaging;
using System.Windows.Controls;
using System.Globalization;
using System.Threading;

namespace AppTray.ViewModels {
    public class MainViewModel : BindableBase {
        /// <summary>
        /// 
        /// </summary>
        public MainViewModel() {
            //管理者アイコン取得
            ShieldImage = ShieldIcon.GetBitmapSource(true);
            PageNavigatorVisibility = Visibility.Collapsed;

            //ホットキー登録
            _hotKey = HotKey.Create();

            //登録情報取得
            _buttonInfo = new ButtonInfo(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location));

            //ドラッグアンドドロップでの移動
            _description = new DragAcceptDescription();
            _description.DragOver += (e) => {
                if (e.AllowedEffects.HasFlag(e.Effects)) {
                    if (e.Data.GetDataPresent(typeof(string))) {
                        return;
                    }
                }
                e.Effects = DragDropEffects.None;
            };
            _description.DragDrop += (e) => {
                MovePagePanelZIndex = -1;
                if (!_buttonInfo.GetButtonInfoAllPage().ContainsKey(e.FromPageNo)) {
                    return;
                }
                if (!_buttonInfo.GetButtonInfoAllPage()[e.FromPageNo].ContainsKey(e.FromButtonNo)) {
                    return;
                }
                var befInfo = _buttonInfo.GetButtonInfoAllPage()[e.FromPageNo][e.FromButtonNo];
                _buttonInfo.Move(e.FromPageNo, e.FromButtonNo, e.ToPageNo, e.ToButtonNo, befInfo);
                RaisePropertyChanged(nameof(ButtonInfo));
            };

            //コマンド作成
            CreateCommand();

            //ショートカット登録
            Shortcuts = new Dictionary<KeyData, ICommand>() {
                {
                    new KeyData() { KeyCode = Key.Right, Modifier=ModifierKeys.Alt },
                    new RelayCommand(() => {
                        _buttonInfo.NextPage();
                        RaisePropertyChanged(nameof(ButtonInfo));
                    })
                },
                {
                    new KeyData() { KeyCode = Key.Left, Modifier=ModifierKeys.Alt },
                    new RelayCommand(() => {
                        _buttonInfo.PreviousPage();
                        RaisePropertyChanged(nameof(ButtonInfo));
                    })
                },
                {
                    new KeyData() { KeyCode = Key.N, Modifier=ModifierKeys.Control },
                    AddPageCommand
                },
                {
                    new KeyData() { KeyCode = Key.D, Modifier=ModifierKeys.Control },
                    DeletePageCommand
                },
            };

            //SystemMenu
            SystemMenuItems = new List<SystemMenuItem>() {
                new SystemMenuItem() {
                    MenuID = 0x0001,
                    MenuName = "ホットキー設定",
                    Command = new RelayCommand(() => {
                        IsShowingDialog = true;

                        using(var vm = new HotKeySettingViewModel(_hotKey)) {
                            Messenger.Raise(new TransitionMessage(vm, "HotKeySettingWindowMessageKey"));
                            if (vm.IsUpdate) {
                                HotKey = new HotKey(){ Key = vm.Key, Modifiers = vm.ModifierKeys };
                            }
                        }

                        IsShowingDialog = false;
                    })
                },
                new SystemMenuItem() {
                    IsSeparator = true
                },
                new SystemMenuItem() {
                    MenuID = 0x0002,
                    MenuName = "新しいページを追加",
                    Command = AddPageCommand
                },
                new SystemMenuItem() {
                    MenuID = 0x0003,
                    MenuName = "現在のページを削除",
                    Command = DeletePageCommand
                },
                new SystemMenuItem() {
                    MenuID = 0x0004,
                    MenuName = "前ページ",
                    Command = new RelayCommand(() => {
                        _buttonInfo.PreviousPage();
                        RaisePropertyChanged(nameof(ButtonInfo));
                    })
                },
                new SystemMenuItem() {
                    MenuID = 0x0005,
                    MenuName = "次ページ",
                    Command = new RelayCommand(() => {
                        _buttonInfo.NextPage();
                        RaisePropertyChanged(nameof(ButtonInfo));
                    })
                }
            };
        }

        private void CreateCommand() {
            WindowCloseCommand = new RelayCommand<Window>((w) => {
                CanCloseWindow = false;
                SystemCommands.CloseWindow(w);
            });

            ApplicationCloseCommand = new RelayCommand<Window>((w) => {
                CanCloseWindow = true;
                SystemCommands.CloseWindow(w);
            });

            FileDropCommand = new RelayCommand<FileDropParameter>((para) => {
                _buttonInfo.AddFromFile(para.ButtonNo, para.FilePath);
                RaisePropertyChanged(nameof(ButtonInfo));
            });

            ExecuteAppCommand = new RelayCommand<string>((buttonNo) => {
                var app = _buttonInfo[(int.Parse(buttonNo))];
                if (app == null) {
                    return;
                }
                if (!app.Exist()) {
                    ConfirmForDeleteNotExistApp(int.Parse(buttonNo));
                    return;
                }
                app.Execute(app.IsAdmin);
            });

            ExecuteAppAsAdminCommand = new RelayCommand<string>((buttonNo) => {
                var app = _buttonInfo[(int.Parse(buttonNo.ToString()))];
                if (app == null) {
                    return;
                }
                if (!app.Exist()) {
                    ConfirmForDeleteNotExistApp(int.Parse(buttonNo));
                    return;
                }
                app.Execute(true);
            });

            ExecuteAppFromAppNameCommand = new RelayCommand<AppInfo>((app) => {
                if (!app.Exist()) {
                    ConfirmForDeleteNotExistApp(_buttonInfo.GetButtonNo(app));
                    return;
                }
                app.Execute(false);
            });

            DeleteAppCommand = new RelayCommand<string>((buttonNo) => {
                _buttonInfo.DeleteAppInfo(int.Parse(buttonNo));
                RaisePropertyChanged(nameof(ButtonInfo));
            });

            CallSettingWindowCommand = new RelayCommand<string>((buttonNo) => {
                IsShowingDialog = true;

                int key = (int.Parse(buttonNo));
                var app = _buttonInfo[key];
                if (app == null) {
                    app = new AppInfo();
                }

                SettingViewModel setting = new SettingViewModel();
                setting.SetAppInfo(app);
                Messenger.Raise(new TransitionMessage(setting, "SettingWindowMessageKey"));

                if (setting.IsUpdate) {
                    _buttonInfo.Add(key, setting.GetAppInfo());
                    RaisePropertyChanged(nameof(ButtonInfo));
                }

                IsShowingDialog = false;
            });

            CallSubWindowCommand = new RelayCommand<string>((buttonNo) => {
                var sub = new SubListViewModel();
                Messenger.Raise(new TransitionMessage(sub, "SubListWindowMessageKey"));
            });

            MovePageCommand = new RelayCommand<bool>((isNext) => {
                if (isNext) {
                    _buttonInfo.NextPage();
                } else {
                    _buttonInfo.PreviousPage();
                }
                RaisePropertyChanged(nameof(ButtonInfo));

                RaisePropertyChanged(nameof(CurrentPageNo));
            });

            AddPageCommand = new RelayCommand(() => {
                _buttonInfo.AddPage();
                RaisePropertyChanged(nameof(ButtonInfo));
                RaisePropertyChanged(nameof(PageCount));
                RaisePropertyChanged(nameof(CurrentPageNo));
            });

            DeletePageCommand = new RelayCommand(() => {
                _buttonInfo.DeletePage();
                RaisePropertyChanged(nameof(ButtonInfo));
                RaisePropertyChanged(nameof(PageCount));
                RaisePropertyChanged(nameof(CurrentPageNo));
            });
        }

        private void ConfirmForDeleteNotExistApp(int buttonNo) {
            IsShowingDialog = true;
            if (MessageBox.Show("ファイルが見つかりません。一覧から削除しますか？", "確認", MessageBoxButton.YesNo) == MessageBoxResult.Yes) {
                _buttonInfo.DeleteAppInfo(buttonNo);
                RaisePropertyChanged(nameof(ButtonInfo));
            }
            IsShowingDialog = false;
        }

        /// <summary>
        /// ファイル追加
        /// </summary>
        public ICommand FileDropCommand { get; set; }
        /// <summary>
        /// Windowを閉じる
        /// </summary>
        public ICommand WindowCloseCommand { get; set; }
        /// <summary>
        /// アプリケーションを閉じる
        /// </summary>
        public ICommand ApplicationCloseCommand { get; set; }
        /// <summary>
        /// 登録されたアプリケーションを実行
        /// </summary>
        public ICommand ExecuteAppCommand { get; set; }
        /// <summary>
        /// 登録されたアプリケーションを管理者として実行
        /// </summary>
        public ICommand ExecuteAppAsAdminCommand { get; set; }
        /// <summary>
        /// 登録されたアプリケーションを実行（アプリケーション名で指定）
        /// </summary>
        public ICommand ExecuteAppFromAppNameCommand { get; set; }
        /// <summary>
        /// アプリケーションを削除
        /// </summary>
        public ICommand DeleteAppCommand { get; set; }
        /// <summary>
        /// アプリケーション設定画面を起動
        /// </summary>
        public ICommand CallSettingWindowCommand { get; set; }
        /// <summary>
        /// 次のページへ／前のページへ
        /// </summary>
        public ICommand MovePageCommand { get; set; }
        /// <summary>
        /// ページ追加
        /// </summary>
        public ICommand AddPageCommand { get; set; }
        /// <summary>
        /// ページ削除
        /// </summary>
        public ICommand DeletePageCommand { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public ICommand CallSubWindowCommand { get; set; }


        private double _top;
        /// <summary>
        /// 初期表示位置
        /// </summary>
        public double Top {
            get {
                return _top;
            }
            set {
                SetProperty(ref _top, value);
            }
        }

        private double _left;
        /// <summary>
        /// 初期表示位置
        /// </summary>
        public double Left {
            get {
                return _left;
            }
            set {
                SetProperty(ref _left, value);
            }
        }

        private bool _canCloseWindow = true;
        /// <summary>
        /// Windowを閉じたときにアプリケーションを終了する
        /// </summary>
        public bool CanCloseWindow {
            get {
                return _canCloseWindow;
            }
            set {
                SetProperty(ref _canCloseWindow, value);
            }
        }

        /// <summary>
        /// アクティブじゃないときもWindowを閉じない
        /// </summary>
        public bool IsShowDeactive {
            get {
                return _isChecked || _isShowingDialog;
            }
        }

        private bool _isChecked;
        /// <summary>
        /// アクティブじゃない時もWindowを閉じないチェックボックスの状態
        /// </summary>
        public bool IsChecked {
            get {
                return _isChecked;
            }
            set {
                SetProperty(ref _isChecked, value);
                RaisePropertyChanged(nameof(IsShowDeactive));
            }
        }

        private bool _isShowingDialog;
        /// <summary>
        /// 何らかのサブダイアログを表示している
        /// </summary>
        public bool IsShowingDialog {
            get {
                return _isShowingDialog;
            }
            set {
                SetProperty(ref _isShowingDialog, value);
                RaisePropertyChanged(nameof(IsShowDeactive));
            }
        }

        private Dictionary<KeyData, ICommand> _shortcuts;
        /// <summary>
        /// ショートカット情報
        /// </summary>
        public Dictionary<KeyData, ICommand> Shortcuts {
            get {
                return _shortcuts;
            }
            set {
                SetProperty(ref _shortcuts, value);
            }
        }

        private Visibility _pageNavigatorVisibility = Visibility.Visible;
        public Visibility PageNavigatorVisibility {
            get {
                return _pageNavigatorVisibility;
            }
            set {
                SetProperty(ref _pageNavigatorVisibility, value);
            }
        }

        private List<SystemMenuItem> _systemMenuItems;
        public List<SystemMenuItem> SystemMenuItems {
            get {
                return _systemMenuItems;
            }
            set {
                SetProperty(ref _systemMenuItems, value);
            }
        }

        public int PageCount {
            get {
                return _buttonInfo.PageCount;
            }
        }

        public int CurrentPageNo {
            get {
                return _buttonInfo.CurrentPageNo;
            }
        }

        private int _movePagePanelZIndex;
        public int MovePagePanelZIndex {
            get {
                return _movePagePanelZIndex;
            }
            set {
                SetProperty(ref _movePagePanelZIndex, value);
            }
        }

        private int _dragedButtonNo;
        public int DragedButtonNo {
            get {
                return _dragedButtonNo;
            }
            set {
                SetProperty(ref _dragedButtonNo, value);
                var app = _buttonInfo[value];
                DragedButtonImage = app?.ImageSource;
                DragedButtonText = app?.AppDisplayName;
            }
        }

        private ImageSource _dragedButtonImage;
        public ImageSource DragedButtonImage {
            get {
                return _dragedButtonImage;
            }
            set {
                SetProperty(ref _dragedButtonImage, value);
            }
        }

        private string _dragedButtonText;
        public string DragedButtonText {
            get {
                return _dragedButtonText;
            }
            set {
                SetProperty(ref _dragedButtonText, value);
            }
        }

        private HotKey _hotKey;
        public HotKey HotKey {
            get {
                return _hotKey;
            }
            set {
                SetProperty(ref _hotKey, value);
            }
        }

        /// <summary>
        /// 管理者アイコン
        /// </summary>
        public BitmapSource ShieldImage { get; }

        private ButtonInfo _buttonInfo;
        /// <summary>
        /// 登録アプリケーション情報
        /// </summary>
        public ButtonInfo ButtonInfo { get { return _buttonInfo; } }

        private DragAcceptDescription _description;
        /// <summary>
        /// ドラッグアンドドロップコントロールクラス
        /// </summary>
        public DragAcceptDescription Description { get { return _description; } }

        /// <summary>
        /// AutoCompleteフィルタリング情報 
        /// </summary>
        public AutoCompleteFilterPredicate<object> ButtonInfoFilter {
            get {
                return (searchText, obj) => {
                    CompareInfo ci = CultureInfo.CurrentCulture.CompareInfo;
                    return ci.IndexOf((obj as AppInfo).AppDisplayName, searchText, CompareOptions.IgnoreWidth | CompareOptions.IgnoreCase | CompareOptions.IgnoreKanaType) >= 0;
                };
            }
        }
    }
}
