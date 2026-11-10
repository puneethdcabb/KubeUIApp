using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using KubeUIApp.Models;
using KubeUIApp.Services;
using KubeUIApp.Views;
using Microsoft.Win32;

namespace KubeUIApp.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly ISshService _sshService;
        private readonly IClusterService _clusterService;
        private readonly ProfileStorageService _profileStorage;
        private readonly DispatcherTimer _autoRefreshTimer;

        // Connection fields
        private bool _isConnected;
        private string _connectionStatusText = "Disconnected";
        private string _connectionStatusColor = "#6C7086";
        private bool _isBusy;
        private string _busyText = "Loading...";

        private string _profileName = "MicroShift Node";
        private string _host = "192.168.1.100";
        private int _port = 22;
        private string _username = "root";
        private string _password = "";
        private string _sudoPassword = "";
        private bool _useSudo = true;
        private string _sshKeyPath = "";
        private string _preferredCli = "Auto";
        private string _preferredContainerRuntime = "Auto";
        private string _customKubeconfig = "";

        private ConnectionProfile? _selectedProfile;
        private NodeStats _nodeStats = new();
        private string _selectedNamespace = "All Namespaces";

        // Search / Filter strings
        private string _podSearchText = "";
        private string _deploymentSearchText = "";
        private string _pvcSearchText = "";
        private string _secretSearchText = "";
        private string _ingressRouteSearchText = "";
        private string _containerSearchText = "";
        private string _imageSearchText = "";
        private string _currentRemotePath = "/";
        private RemoteFileInfo? _selectedRemoteFile;

        // Command log drawer
        private bool _isCommandLogVisible = false;
        private string _customCommandText = "";

        // Auto Refresh
        private string _selectedRefreshInterval = "Manual";
        private bool _isDarkTheme = true;

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set
            {
                if (SetField(ref _isDarkTheme, value))
                {
                    OnPropertyChanged(nameof(ThemeButtonText));
                    ThemeService.ApplyTheme(value);
                }
            }
        }

        public string ThemeButtonText => IsDarkTheme ? "☀️ Light Mode" : "🌙 Dark Mode";

        public ObservableCollection<ConnectionProfile> Profiles { get; } = new();
        public ObservableCollection<string> Namespaces { get; } = new();
        public ObservableCollection<PodItem> Pods { get; } = new();
        public ObservableCollection<DeploymentItem> Deployments { get; } = new();
        public ObservableCollection<PvcItem> Pvcs { get; } = new();
        public ObservableCollection<SecretItem> Secrets { get; } = new();
        public ObservableCollection<IngressRouteItem> IngressRoutes { get; } = new();
        public ObservableCollection<ContainerItem> Containers { get; } = new();
        public ObservableCollection<DockerImageItem> Images { get; } = new();
        public ObservableCollection<RemoteFileInfo> RemoteFiles { get; } = new();
        public ObservableCollection<CommandLogEntry> CommandLogs { get; } = new();

        public ICollectionView FilteredPods { get; }
        public ICollectionView FilteredDeployments { get; }
        public ICollectionView FilteredPvcs { get; }
        public ICollectionView FilteredSecrets { get; }
        public ICollectionView FilteredIngressRoutes { get; }
        public ICollectionView FilteredContainers { get; }
        public ICollectionView FilteredImages { get; }

        private PodItem? _selectedPod;
        public PodItem? SelectedPod
        {
            get => _selectedPod;
            set => SetField(ref _selectedPod, value);
        }

        private DeploymentItem? _selectedDeployment;
        public DeploymentItem? SelectedDeployment
        {
            get => _selectedDeployment;
            set => SetField(ref _selectedDeployment, value);
        }

        private PvcItem? _selectedPvc;
        public PvcItem? SelectedPvc
        {
            get => _selectedPvc;
            set => SetField(ref _selectedPvc, value);
        }

        private SecretItem? _selectedSecret;
        public SecretItem? SelectedSecret
        {
            get => _selectedSecret;
            set => SetField(ref _selectedSecret, value);
        }

        private IngressRouteItem? _selectedIngressRoute;
        public IngressRouteItem? SelectedIngressRoute
        {
            get => _selectedIngressRoute;
            set => SetField(ref _selectedIngressRoute, value);
        }

        private RouterStatusInfo _routerStatus = new();
        public RouterStatusInfo RouterStatus
        {
            get => _routerStatus;
            set => SetField(ref _routerStatus, value);
        }

        private ContainerItem? _selectedContainer;
        public ContainerItem? SelectedContainer
        {
            get => _selectedContainer;
            set => SetField(ref _selectedContainer, value);
        }

        private DockerImageItem? _selectedImage;
        public DockerImageItem? SelectedImage
        {
            get => _selectedImage;
            set => SetField(ref _selectedImage, value);
        }

        // Properties
        public bool IsConnected
        {
            get => _isConnected;
            set => SetField(ref _isConnected, value);
        }

        public string ConnectionStatusText
        {
            get => _connectionStatusText;
            set => SetField(ref _connectionStatusText, value);
        }

        public string ConnectionStatusColor
        {
            get => _connectionStatusColor;
            set => SetField(ref _connectionStatusColor, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set => SetField(ref _isBusy, value);
        }

        public string BusyText
        {
            get => _busyText;
            set => SetField(ref _busyText, value);
        }

        public string ProfileName
        {
            get => _profileName;
            set => SetField(ref _profileName, value);
        }

        public string Host
        {
            get => _host;
            set => SetField(ref _host, value);
        }

        public int Port
        {
            get => _port;
            set => SetField(ref _port, value);
        }

        public string Username
        {
            get => _username;
            set => SetField(ref _username, value);
        }

        public string Password
        {
            get => _password;
            set => SetField(ref _password, value);
        }

        public string SudoPassword
        {
            get => _sudoPassword;
            set => SetField(ref _sudoPassword, value);
        }

        public bool UseSudo
        {
            get => _useSudo;
            set => SetField(ref _useSudo, value);
        }

        public string SshKeyPath
        {
            get => _sshKeyPath;
            set => SetField(ref _sshKeyPath, value);
        }

        public string PreferredCli
        {
            get => _preferredCli;
            set => SetField(ref _preferredCli, value);
        }

        public string PreferredContainerRuntime
        {
            get => _preferredContainerRuntime;
            set => SetField(ref _preferredContainerRuntime, value);
        }

        public string CustomKubeconfig
        {
            get => _customKubeconfig;
            set => SetField(ref _customKubeconfig, value);
        }

        public ConnectionProfile? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (SetField(ref _selectedProfile, value) && value != null)
                {
                    ProfileName = value.ProfileName;
                    Host = value.Host;
                    Port = value.Port;
                    Username = value.Username;
                    Password = value.Password;
                    SudoPassword = value.SudoPassword;
                    UseSudo = value.UseSudo;
                    SshKeyPath = value.SshKeyPath ?? "";
                    PreferredCli = value.PreferredCli;
                    PreferredContainerRuntime = value.PreferredContainerRuntime;
                    CustomKubeconfig = value.CustomKubeconfig;
                }
            }
        }

        public NodeStats NodeStats
        {
            get => _nodeStats;
            set => SetField(ref _nodeStats, value);
        }

        private bool _suppressNamespaceRefresh;

        public string SelectedNamespace
        {
            get => _selectedNamespace;
            set
            {
                if (SetField(ref _selectedNamespace, value) && !_suppressNamespaceRefresh && !string.IsNullOrEmpty(value))
                {
                    _ = RefreshWorkloadsAsync();
                }
            }
        }

        public string PodSearchText
        {
            get => _podSearchText;
            set
            {
                if (SetField(ref _podSearchText, value))
                {
                    FilteredPods.Refresh();
                }
            }
        }

        public string DeploymentSearchText
        {
            get => _deploymentSearchText;
            set
            {
                if (SetField(ref _deploymentSearchText, value))
                {
                    FilteredDeployments.Refresh();
                }
            }
        }

        public string PvcSearchText
        {
            get => _pvcSearchText;
            set
            {
                if (SetField(ref _pvcSearchText, value))
                {
                    FilteredPvcs.Refresh();
                }
            }
        }

        public string SecretSearchText
        {
            get => _secretSearchText;
            set
            {
                if (SetField(ref _secretSearchText, value))
                {
                    FilteredSecrets.Refresh();
                }
            }
        }

        public string IngressRouteSearchText
        {
            get => _ingressRouteSearchText;
            set
            {
                if (SetField(ref _ingressRouteSearchText, value))
                {
                    FilteredIngressRoutes.Refresh();
                }
            }
        }

        public string ContainerSearchText
        {
            get => _containerSearchText;
            set
            {
                if (SetField(ref _containerSearchText, value))
                {
                    FilteredContainers.Refresh();
                }
            }
        }

        public string ImageSearchText
        {
            get => _imageSearchText;
            set
            {
                if (SetField(ref _imageSearchText, value))
                {
                    FilteredImages.Refresh();
                }
            }
        }

        public string CurrentRemotePath
        {
            get => _currentRemotePath;
            set => SetField(ref _currentRemotePath, value);
        }

        public RemoteFileInfo? SelectedRemoteFile
        {
            get => _selectedRemoteFile;
            set => SetField(ref _selectedRemoteFile, value);
        }

        public bool IsCommandLogVisible
        {
            get => _isCommandLogVisible;
            set => SetField(ref _isCommandLogVisible, value);
        }

        public string CustomCommandText
        {
            get => _customCommandText;
            set => SetField(ref _customCommandText, value);
        }

        public string SelectedRefreshInterval
        {
            get => _selectedRefreshInterval;
            set
            {
                if (SetField(ref _selectedRefreshInterval, value))
                {
                    UpdateAutoRefreshTimer();
                }
            }
        }

        // Summary Statistics Properties
        public int TotalPodsCount => Pods.Count;
        public int RunningPodsCount => Pods.Count(p => p.Status.Equals("Running", StringComparison.OrdinalIgnoreCase));
        public int PendingPodsCount => Pods.Count(p => p.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase) || p.Status.Equals("ContainerCreating", StringComparison.OrdinalIgnoreCase));
        public int FailedPodsCount => Pods.Count(p => p.Status.Contains("Error", StringComparison.OrdinalIgnoreCase) || p.Status.Contains("BackOff", StringComparison.OrdinalIgnoreCase) || p.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase));
        public int TotalDeploymentsCount => Deployments.Count;
        public int TotalPvcsCount => Pvcs.Count;
        public int TotalSecretsCount => Secrets.Count;
        public int TotalRoutesCount => IngressRoutes.Count;
        public int TotalContainersCount => Containers.Count;
        public int TotalImagesCount => Images.Count;

        // Commands
        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand SaveProfileCommand { get; }
        public ICommand NewProfileCommand { get; }
        public ICommand DeleteProfileCommand { get; }
        public ICommand RefreshAllCommand { get; }

        // Pod Commands
        public ICommand RefreshPodsCommand { get; }
        public ICommand ViewPodLogsCommand { get; }
        public ICommand ViewPodYamlCommand { get; }
        public ICommand CopyPodYamlCommand { get; }
        public ICommand DescribePodCommand { get; }
        public ICommand RestartPodCommand { get; }
        public ICommand RedeployPodCommand { get; }
        public ICommand StopPodCommand { get; }
        public ICommand DeletePodCommand { get; }
        public ICommand ExecPodCommand { get; }
        public ICommand BrowsePodFilesCommand { get; }
        public ICommand OpenPodAttachedPvcCommand { get; }
        public ICommand PortForwardCommand { get; }

        // Ingress & Route Commands
        public ICommand RefreshIngressRoutesCommand { get; }
        public ICommand RefreshRouterStatusCommand { get; }
        public ICommand OpenRouteInBrowserCommand { get; }
        public ICommand EditIngressRouteYamlCommand { get; }
        public ICommand DescribeIngressRouteCommand { get; }
        public ICommand DeleteIngressRouteCommand { get; }

        // Deployment Commands
        public ICommand RefreshDeploymentsCommand { get; }
        public ICommand ViewDeploymentYamlCommand { get; }
        public ICommand EditDeploymentYamlCommand { get; }
        public ICommand CopyDeploymentYamlCommand { get; }
        public ICommand DescribeDeploymentCommand { get; }
        public ICommand ScaleDeploymentCommand { get; }
        public ICommand RestartDeploymentCommand { get; }
        public ICommand DeleteDeploymentCommand { get; }

        // PVC Commands
        public ICommand RefreshPvcsCommand { get; }
        public ICommand ViewPvcYamlCommand { get; }
        public ICommand CopyPvcYamlCommand { get; }
        public ICommand DescribePvcCommand { get; }
        public ICommand DeletePvcCommand { get; }
        public ICommand BrowsePvcFilesCommand { get; }

        // Secret Commands
        public ICommand RefreshSecretsCommand { get; }
        public ICommand ViewSecretDataCommand { get; }
        public ICommand ViewSecretYamlCommand { get; }
        public ICommand EditSecretYamlCommand { get; }
        public ICommand CopySecretYamlCommand { get; }
        public ICommand DescribeSecretCommand { get; }
        public ICommand DeleteSecretCommand { get; }

        // Container Commands
        public ICommand RefreshContainersCommand { get; }
        public ICommand ViewContainerLogsCommand { get; }
        public ICommand StartContainerCommand { get; }
        public ICommand StopContainerCommand { get; }
        public ICommand RestartContainerCommand { get; }
        public ICommand RemoveContainerCommand { get; }

        // Image Commands
        public ICommand RefreshImagesCommand { get; }
        public ICommand DeleteImageCommand { get; }
        public ICommand PullImageCommand { get; }

        // Remote Files Commands
        public ICommand RefreshFilesCommand { get; }
        public ICommand OpenRemoteItemCommand { get; }
        public ICommand GoUpDirectoryCommand { get; }
        public ICommand DownloadRemoteFileCommand { get; }
        public ICommand UploadRemoteFileCommand { get; }

        // Transparency / Behind-the-Scenes Drawer Commands
        public ICommand ToggleCommandLogCommand { get; }
        public ICommand ClearCommandLogCommand { get; }
        public ICommand RunCustomCommand { get; }
        public ICommand ToggleThemeCommand { get; }

        public MainViewModel()
        {
            _sshService = new SshService();
            _clusterService = new ClusterService(_sshService);
            _profileStorage = new ProfileStorageService();

            _sshService.ConnectionStatusChanged += (connected, msg) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsConnected = connected;
                    ConnectionStatusText = msg;
                    ConnectionStatusColor = connected ? "#10B981" : "#F38BA8";
                });
            };

            _sshService.CommandExecuted += entry =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    CommandLogs.Insert(0, entry);
                    if (CommandLogs.Count > 150)
                    {
                        CommandLogs.RemoveAt(CommandLogs.Count - 1);
                    }
                });
            };

            // Collection views with filtering
            FilteredPods = CollectionViewSource.GetDefaultView(Pods);
            FilteredPods.Filter = obj =>
            {
                if (obj is not PodItem p) return false;
                if (string.IsNullOrWhiteSpace(_podSearchText)) return true;
                return p.Name.Contains(_podSearchText, StringComparison.OrdinalIgnoreCase) ||
                       p.Namespace.Contains(_podSearchText, StringComparison.OrdinalIgnoreCase) ||
                       p.Status.Contains(_podSearchText, StringComparison.OrdinalIgnoreCase) ||
                       p.NodeName.Contains(_podSearchText, StringComparison.OrdinalIgnoreCase);
            };

            FilteredDeployments = CollectionViewSource.GetDefaultView(Deployments);
            FilteredDeployments.Filter = obj =>
            {
                if (obj is not DeploymentItem d) return false;
                if (string.IsNullOrWhiteSpace(_deploymentSearchText)) return true;
                return d.Name.Contains(_deploymentSearchText, StringComparison.OrdinalIgnoreCase) ||
                       d.Namespace.Contains(_deploymentSearchText, StringComparison.OrdinalIgnoreCase) ||
                       d.Images.Contains(_deploymentSearchText, StringComparison.OrdinalIgnoreCase);
            };

            FilteredPvcs = CollectionViewSource.GetDefaultView(Pvcs);
            FilteredPvcs.Filter = obj =>
            {
                if (obj is not PvcItem p) return false;
                if (string.IsNullOrWhiteSpace(_pvcSearchText)) return true;
                return p.Name.Contains(_pvcSearchText, StringComparison.OrdinalIgnoreCase) ||
                       p.Namespace.Contains(_pvcSearchText, StringComparison.OrdinalIgnoreCase) ||
                       p.VolumeName.Contains(_pvcSearchText, StringComparison.OrdinalIgnoreCase);
            };

            FilteredSecrets = CollectionViewSource.GetDefaultView(Secrets);
            FilteredSecrets.Filter = obj =>
            {
                if (obj is not SecretItem s) return false;
                if (string.IsNullOrWhiteSpace(_secretSearchText)) return true;
                return s.Name.Contains(_secretSearchText, StringComparison.OrdinalIgnoreCase) ||
                       s.Namespace.Contains(_secretSearchText, StringComparison.OrdinalIgnoreCase) ||
                       s.Type.Contains(_secretSearchText, StringComparison.OrdinalIgnoreCase) ||
                       s.KeysDisplay.Contains(_secretSearchText, StringComparison.OrdinalIgnoreCase);
            };

            FilteredIngressRoutes = CollectionViewSource.GetDefaultView(IngressRoutes);
            FilteredIngressRoutes.Filter = obj =>
            {
                if (obj is not IngressRouteItem r) return false;
                if (string.IsNullOrWhiteSpace(_ingressRouteSearchText)) return true;
                return r.Name.Contains(_ingressRouteSearchText, StringComparison.OrdinalIgnoreCase) ||
                       r.Namespace.Contains(_ingressRouteSearchText, StringComparison.OrdinalIgnoreCase) ||
                       r.Host.Contains(_ingressRouteSearchText, StringComparison.OrdinalIgnoreCase) ||
                       r.ServiceName.Contains(_ingressRouteSearchText, StringComparison.OrdinalIgnoreCase) ||
                       r.Kind.Contains(_ingressRouteSearchText, StringComparison.OrdinalIgnoreCase);
            };

            FilteredContainers = CollectionViewSource.GetDefaultView(Containers);
            FilteredContainers.Filter = obj =>
            {
                if (obj is not ContainerItem c) return false;
                if (string.IsNullOrWhiteSpace(_containerSearchText)) return true;
                return c.Name.Contains(_containerSearchText, StringComparison.OrdinalIgnoreCase) ||
                       c.Image.Contains(_containerSearchText, StringComparison.OrdinalIgnoreCase) ||
                       c.State.Contains(_containerSearchText, StringComparison.OrdinalIgnoreCase) ||
                       c.Id.Contains(_containerSearchText, StringComparison.OrdinalIgnoreCase);
            };

            FilteredImages = CollectionViewSource.GetDefaultView(Images);
            FilteredImages.Filter = obj =>
            {
                if (obj is not DockerImageItem img) return false;
                if (string.IsNullOrWhiteSpace(_imageSearchText)) return true;
                return img.FullName.Contains(_imageSearchText, StringComparison.OrdinalIgnoreCase) ||
                       img.Id.Contains(_imageSearchText, StringComparison.OrdinalIgnoreCase);
            };

            // Initialize commands
            ConnectCommand = new AsyncRelayCommand(ConnectAsync);
            DisconnectCommand = new RelayCommand(Disconnect);
            SaveProfileCommand = new RelayCommand(SaveCurrentProfile);
            NewProfileCommand = new RelayCommand(NewProfile);
            DeleteProfileCommand = new RelayCommand(DeleteCurrentProfile);
            RefreshAllCommand = new AsyncRelayCommand(RefreshAllAsync);

            RefreshPodsCommand = new AsyncRelayCommand(RefreshPodsAsync);
            ViewPodLogsCommand = new AsyncRelayCommand(ViewPodLogsAsync);
            ViewPodYamlCommand = new AsyncRelayCommand(ViewPodYamlAsync);
            CopyPodYamlCommand = new AsyncRelayCommand(CopyPodYamlAsync);
            DescribePodCommand = new AsyncRelayCommand(DescribePodAsync);
            RestartPodCommand = new AsyncRelayCommand(RestartPodAsync);
            RedeployPodCommand = new AsyncRelayCommand(RedeployPodAsync);
            StopPodCommand = new AsyncRelayCommand(StopPodAsync);
            DeletePodCommand = new AsyncRelayCommand(DeletePodAsync);
            ExecPodCommand = new RelayCommand(ExecInPod);
            BrowsePodFilesCommand = new RelayCommand(BrowsePodFiles);
            OpenPodAttachedPvcCommand = new AsyncRelayCommand(OpenPodAttachedPvcAsync);
            PortForwardCommand = new RelayCommand(OpenPortForward);

            RefreshIngressRoutesCommand = new AsyncRelayCommand(RefreshIngressRoutesAsync);
            RefreshRouterStatusCommand = new AsyncRelayCommand(RefreshRouterStatusAsync);
            OpenRouteInBrowserCommand = new RelayCommand(OpenRouteInBrowser);
            EditIngressRouteYamlCommand = new AsyncRelayCommand(EditIngressRouteYamlAsync);
            DescribeIngressRouteCommand = new AsyncRelayCommand(DescribeIngressRouteAsync);
            DeleteIngressRouteCommand = new AsyncRelayCommand(DeleteIngressRouteAsync);

            RefreshDeploymentsCommand = new AsyncRelayCommand(RefreshDeploymentsAsync);
            ViewDeploymentYamlCommand = new AsyncRelayCommand(ViewDeploymentYamlAsync);
            EditDeploymentYamlCommand = new AsyncRelayCommand(EditDeploymentYamlAsync);
            CopyDeploymentYamlCommand = new AsyncRelayCommand(CopyDeploymentYamlAsync);
            DescribeDeploymentCommand = new AsyncRelayCommand(DescribeDeploymentAsync);
            ScaleDeploymentCommand = new AsyncRelayCommand(ScaleDeploymentAsync);
            RestartDeploymentCommand = new AsyncRelayCommand(RestartDeploymentAsync);
            DeleteDeploymentCommand = new AsyncRelayCommand(DeleteDeploymentAsync);

            RefreshPvcsCommand = new AsyncRelayCommand(RefreshPvcsAsync);
            ViewPvcYamlCommand = new AsyncRelayCommand(ViewPvcYamlAsync);
            CopyPvcYamlCommand = new AsyncRelayCommand(CopyPvcYamlAsync);
            DescribePvcCommand = new AsyncRelayCommand(DescribePvcAsync);
            DeletePvcCommand = new AsyncRelayCommand(DeletePvcAsync);
            BrowsePvcFilesCommand = new RelayCommand(BrowsePvcFiles);

            RefreshSecretsCommand = new AsyncRelayCommand(RefreshSecretsAsync);
            ViewSecretDataCommand = new RelayCommand(ViewSecretData);
            ViewSecretYamlCommand = new AsyncRelayCommand(ViewSecretYamlAsync);
            EditSecretYamlCommand = new AsyncRelayCommand(EditSecretYamlAsync);
            CopySecretYamlCommand = new AsyncRelayCommand(CopySecretYamlAsync);
            DescribeSecretCommand = new AsyncRelayCommand(DescribeSecretAsync);
            DeleteSecretCommand = new AsyncRelayCommand(DeleteSecretAsync);

            RefreshContainersCommand = new AsyncRelayCommand(RefreshContainersAsync);
            ViewContainerLogsCommand = new AsyncRelayCommand(ViewContainerLogsAsync);
            StartContainerCommand = new AsyncRelayCommand(StartContainerAsync);
            StopContainerCommand = new AsyncRelayCommand(StopContainerAsync);
            RestartContainerCommand = new AsyncRelayCommand(RestartContainerAsync);
            RemoveContainerCommand = new AsyncRelayCommand(RemoveContainerAsync);

            RefreshImagesCommand = new AsyncRelayCommand(RefreshImagesAsync);
            DeleteImageCommand = new AsyncRelayCommand(DeleteImageAsync);
            PullImageCommand = new AsyncRelayCommand(PullImageAsync);

            RefreshFilesCommand = new AsyncRelayCommand(RefreshFilesAsync);
            OpenRemoteItemCommand = new AsyncRelayCommand(OpenRemoteItemAsync);
            GoUpDirectoryCommand = new AsyncRelayCommand(GoUpDirectoryAsync);
            DownloadRemoteFileCommand = new AsyncRelayCommand(DownloadRemoteFileAsync);
            UploadRemoteFileCommand = new AsyncRelayCommand(UploadRemoteFileAsync);

            ToggleCommandLogCommand = new RelayCommand(() => IsCommandLogVisible = !IsCommandLogVisible);
            ClearCommandLogCommand = new RelayCommand(() => CommandLogs.Clear());
            RunCustomCommand = new AsyncRelayCommand(ExecuteCustomCommandAsync);
            ToggleThemeCommand = new RelayCommand(() => IsDarkTheme = !IsDarkTheme);

            // Auto-refresh timer
            _autoRefreshTimer = new DispatcherTimer();
            _autoRefreshTimer.Tick += async (s, e) =>
            {
                if (IsConnected && !IsBusy)
                {
                    await RefreshAllAsync();
                }
            };

            // Load saved profiles
            LoadProfiles();
        }

        private void LoadProfiles()
        {
            var list = _profileStorage.LoadProfiles();
            Profiles.Clear();
            foreach (var p in list)
            {
                Profiles.Add(p);
            }
            if (Profiles.Count > 0)
            {
                SelectedProfile = Profiles[0];
            }
        }

        private void SaveCurrentProfile()
        {
            if (SelectedProfile == null)
            {
                SelectedProfile = new ConnectionProfile();
                Profiles.Add(SelectedProfile);
            }

            SelectedProfile.ProfileName = ProfileName;
            SelectedProfile.Host = Host;
            SelectedProfile.Port = Port;
            SelectedProfile.Username = Username;
            SelectedProfile.Password = Password;
            SelectedProfile.SudoPassword = SudoPassword;
            SelectedProfile.UseSudo = UseSudo;
            SelectedProfile.SshKeyPath = SshKeyPath;
            SelectedProfile.PreferredCli = PreferredCli;
            SelectedProfile.PreferredContainerRuntime = PreferredContainerRuntime;
            SelectedProfile.CustomKubeconfig = CustomKubeconfig;

            _profileStorage.SaveProfiles(Profiles.ToList());
            MessageBox.Show("Profile saved successfully!", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void NewProfile()
        {
            var newProf = new ConnectionProfile
            {
                ProfileName = $"Node {Profiles.Count + 1}",
                Host = "192.168.1.100",
                Port = 22,
                Username = "root"
            };
            Profiles.Add(newProf);
            SelectedProfile = newProf;
        }

        private void DeleteCurrentProfile()
        {
            if (SelectedProfile != null)
            {
                Profiles.Remove(SelectedProfile);
                _profileStorage.SaveProfiles(Profiles.ToList());
                SelectedProfile = Profiles.FirstOrDefault();
            }
        }

        private async Task ConnectAsync()
        {
            IsBusy = true;
            BusyText = $"Connecting to {Host}:{Port} via SSH...";

            var profile = new ConnectionProfile
            {
                ProfileName = ProfileName,
                Host = Host.Trim(),
                Port = Port,
                Username = Username.Trim(),
                Password = Password,
                SudoPassword = SudoPassword,
                UseSudo = UseSudo,
                SshKeyPath = SshKeyPath?.Trim(),
                PreferredCli = PreferredCli,
                PreferredContainerRuntime = PreferredContainerRuntime,
                CustomKubeconfig = CustomKubeconfig?.Trim() ?? ""
            };

            var (success, message) = await _sshService.ConnectAsync(profile);
            if (!success)
            {
                IsBusy = false;
                MessageBox.Show($"Failed to connect:\n{message}", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            BusyText = "Detecting OpenShift / MicroShift / Kubernetes & Container tools...";
            await _clusterService.InitializeAsync();

            BusyText = "Loading cluster namespaces and resources...";
            await LoadNamespacesAsync();
            await RefreshAllAsync();

            IsBusy = false;
        }

        private void Disconnect()
        {
            _sshService.Disconnect();
            Pods.Clear();
            Deployments.Clear();
            Pvcs.Clear();
            Secrets.Clear();
            IngressRoutes.Clear();
            Containers.Clear();
            Images.Clear();
            RemoteFiles.Clear();
            Namespaces.Clear();
            NodeStats = new NodeStats();
            RouterStatus = new RouterStatusInfo();
            NotifyStatsChanged();
        }

        private async Task LoadNamespacesAsync()
        {
            var nsList = await _clusterService.GetNamespacesAsync();
            _suppressNamespaceRefresh = true;
            try
            {
                string previousNs = _selectedNamespace;
                Namespaces.Clear();
                foreach (var ns in nsList)
                {
                    Namespaces.Add(ns);
                }
                _selectedNamespace = (!string.IsNullOrEmpty(previousNs) && Namespaces.Contains(previousNs))
                    ? previousNs
                    : (Namespaces.FirstOrDefault() ?? "All Namespaces");
                OnPropertyChanged(nameof(SelectedNamespace));
            }
            finally
            {
                _suppressNamespaceRefresh = false;
            }
        }

        private async Task RefreshAllAsync()
        {
            if (!IsConnected) return;

            IsBusy = true;
            BusyText = "Refreshing cluster workloads and stats...";

            try
            {
                var statsTask = _clusterService.GetNodeStatsAsync();
                var podsTask = _clusterService.GetPodsAsync(SelectedNamespace);
                var depsTask = _clusterService.GetDeploymentsAsync(SelectedNamespace);
                var pvcsTask = _clusterService.GetPvcsAsync(SelectedNamespace);
                var secretsTask = _clusterService.GetSecretsAsync(SelectedNamespace);
                var routesTask = _clusterService.GetIngressRoutesAsync(SelectedNamespace);
                var routerStatusTask = _clusterService.GetRouterStatusAsync();
                var containersTask = _clusterService.GetContainersAsync(true);
                var imagesTask = _clusterService.GetImagesAsync();
                var filesTask = _sshService.ListDirectoryAsync(CurrentRemotePath);

                await Task.WhenAll(statsTask, podsTask, depsTask, pvcsTask, secretsTask, routesTask, routerStatusTask, containersTask, imagesTask, filesTask);

                NodeStats = await statsTask;
                RouterStatus = await routerStatusTask;

                Pods.Clear();
                foreach (var p in await podsTask) Pods.Add(p);

                Deployments.Clear();
                foreach (var d in await depsTask) Deployments.Add(d);

                Pvcs.Clear();
                foreach (var pvc in await pvcsTask) Pvcs.Add(pvc);

                Secrets.Clear();
                foreach (var s in await secretsTask) Secrets.Add(s);

                IngressRoutes.Clear();
                foreach (var r in await routesTask) IngressRoutes.Add(r);

                Containers.Clear();
                foreach (var c in await containersTask) Containers.Add(c);

                Images.Clear();
                foreach (var img in await imagesTask) Images.Add(img);

                RemoteFiles.Clear();
                foreach (var f in await filesTask) RemoteFiles.Add(f);

                NotifyStatsChanged();
            }
            catch (Exception ex)
            {
                AppLogger.LogError("MainViewModel.RefreshAllAsync", "Failed to refresh cluster workloads and stats.", ex);
                MessageBox.Show($"Refresh failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RefreshWorkloadsAsync()
        {
            if (!IsConnected) return;
            IsBusy = true;
            BusyText = $"Fetching workloads for namespace '{SelectedNamespace}'...";
            try
            {
                var podsTask = _clusterService.GetPodsAsync(SelectedNamespace);
                var depsTask = _clusterService.GetDeploymentsAsync(SelectedNamespace);
                var pvcsTask = _clusterService.GetPvcsAsync(SelectedNamespace);
                var secretsTask = _clusterService.GetSecretsAsync(SelectedNamespace);
                var routesTask = _clusterService.GetIngressRoutesAsync(SelectedNamespace);

                await Task.WhenAll(podsTask, depsTask, pvcsTask, secretsTask, routesTask);

                Pods.Clear();
                foreach (var p in await podsTask) Pods.Add(p);

                Deployments.Clear();
                foreach (var d in await depsTask) Deployments.Add(d);

                Pvcs.Clear();
                foreach (var pvc in await pvcsTask) Pvcs.Add(pvc);

                Secrets.Clear();
                foreach (var s in await secretsTask) Secrets.Add(s);

                IngressRoutes.Clear();
                foreach (var r in await routesTask) IngressRoutes.Add(r);

                NotifyStatsChanged();
            }
            catch (Exception ex)
            {
                AppLogger.LogError("MainViewModel.RefreshWorkloadsAsync", $"Failed to refresh workloads for namespace '{SelectedNamespace}'.", ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void NotifyStatsChanged()
        {
            OnPropertyChanged(nameof(TotalPodsCount));
            OnPropertyChanged(nameof(RunningPodsCount));
            OnPropertyChanged(nameof(PendingPodsCount));
            OnPropertyChanged(nameof(FailedPodsCount));
            OnPropertyChanged(nameof(TotalDeploymentsCount));
            OnPropertyChanged(nameof(TotalPvcsCount));
            OnPropertyChanged(nameof(TotalSecretsCount));
            OnPropertyChanged(nameof(TotalRoutesCount));
            OnPropertyChanged(nameof(TotalContainersCount));
            OnPropertyChanged(nameof(TotalImagesCount));
        }

        // Pod Actions
        private async Task RefreshPodsAsync()
        {
            if (!IsConnected) return;
            IsBusy = true;
            BusyText = "Refreshing Pods...";
            try
            {
                var list = await _clusterService.GetPodsAsync(SelectedNamespace);
                Pods.Clear();
                foreach (var p in list) Pods.Add(p);
                NotifyStatsChanged();
            }
            finally { IsBusy = false; }
        }

        private async Task ViewPodLogsAsync()
        {
            if (SelectedPod == null) return;
            var pod = SelectedPod;

            var dlg = new LogViewerDialog(
                $"Logs - Pod: {pod.Name} ({pod.Namespace})",
                pod.Containers,
                (container, lines, prev) => _clusterService.GetPodLogsAsync(pod.Name, pod.Namespace, container, lines, prev)
            )
            {
                Owner = Application.Current.MainWindow
            };
            dlg.ShowDialog();
            await Task.CompletedTask;
        }

        private async Task ViewPodYamlAsync()
        {
            if (SelectedPod == null) return;
            var pod = SelectedPod;
            IsBusy = true;
            BusyText = $"Fetching editable YAML for pod '{pod.Name}'...";
            try
            {
                var (resourceTitle, yaml) = await _clusterService.GetPodEditableYamlAsync(pod);
                var dlg = new YamlEditorDialog(resourceTitle, pod.Namespace, yaml, _clusterService, pod)
                {
                    Owner = Application.Current.MainWindow
                };
                if (dlg.ShowDialog() == true && dlg.WasApplied)
                {
                    await Task.Delay(1500);
                    await RefreshPodsAsync();
                    await RefreshDeploymentsAsync();
                }
            }
            finally { IsBusy = false; }
        }

        private async Task CopyPodYamlAsync()
        {
            if (SelectedPod == null) return;
            var sfd = new SaveFileDialog
            {
                FileName = $"{SelectedPod.Name}.yaml",
                Filter = "YAML Files (*.yaml;*.yml)|*.yaml;*.yml|All Files (*.*)|*.*"
            };
            if (sfd.ShowDialog() == true)
            {
                IsBusy = true;
                BusyText = $"Saving pod YAML for '{SelectedPod.Name}'...";
                try
                {
                    string yaml = await _clusterService.GetPodYamlAsync(SelectedPod.Name, SelectedPod.Namespace);
                    await System.IO.File.WriteAllTextAsync(sfd.FileName, yaml);
                    MessageBox.Show($"Pod YAML saved successfully to:\n{sfd.FileName}", "Saved to Windows", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save YAML: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally { IsBusy = false; }
            }
        }

        private void BrowsePodFiles()
        {
            if (SelectedPod == null) return;
            var dlg = new PodFileExplorerDialog(SelectedPod, _clusterService)
            {
                Owner = Application.Current.MainWindow
            };
            dlg.ShowDialog();
        }

        private async Task OpenPodAttachedPvcAsync()
        {
            if (SelectedPod == null) return;
            IsBusy = true;
            BusyText = $"Finding PVCs attached to pod '{SelectedPod.Name}'...";
            try
            {
                var attachedClaims = await _clusterService.GetPodAttachedPvcsAsync(SelectedPod.Name, SelectedPod.Namespace);
                if (attachedClaims.Count == 0)
                {
                    MessageBox.Show(
                        $"Pod '{SelectedPod.Name}' does not have any PersistentVolumeClaims attached in its spec.volumes.",
                        "No Attached PVCs",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                string claimName = attachedClaims[0];
                var matchedPvc = Pvcs.FirstOrDefault(p => p.Name == claimName && p.Namespace == SelectedPod.Namespace)
                    ?? new PvcItem { Name = claimName, Namespace = SelectedPod.Namespace };

                var dlg = new PvcExplorerDialog(matchedPvc, _clusterService)
                {
                    Owner = Application.Current.MainWindow
                };
                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to inspect pod PVCs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void OpenPortForward()
        {
            if (SelectedPod == null) return;
            var dlg = new PortForwardDialog(SelectedPod, _sshService)
            {
                Owner = Application.Current.MainWindow
            };
            dlg.ShowDialog();
        }

        private async Task DescribePodAsync()
        {
            if (SelectedPod == null) return;
            IsBusy = true;
            BusyText = $"Describing pod '{SelectedPod.Name}'...";
            try
            {
                string desc = await _clusterService.DescribePodAsync(SelectedPod.Name, SelectedPod.Namespace);
                var dlg = new YamlViewerDialog($"Describe - {SelectedPod.Namespace}/{SelectedPod.Name}", desc)
                {
                    Owner = Application.Current.MainWindow
                };
                dlg.ShowDialog();
            }
            finally { IsBusy = false; }
        }

        private async Task RestartPodAsync()
        {
            if (SelectedPod == null) return;
            var dlg = new ConfirmDialog("Restart Pod", $"Are you sure you want to restart pod '{SelectedPod.Name}'?", confirmButtonText: "Restart");
            dlg.Owner = Application.Current.MainWindow;
            if (dlg.ShowDialog() == true && dlg.IsConfirmed)
            {
                IsBusy = true;
                BusyText = $"Restarting pod '{SelectedPod.Name}'...";
                try
                {
                    bool ok = await _clusterService.RestartPodAsync(SelectedPod);
                    if (ok)
                    {
                        await Task.Delay(1500);
                        await RefreshPodsAsync();
                    }
                    else
                    {
                        MessageBox.Show("Failed to restart pod. Check Command Logs for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                finally { IsBusy = false; }
            }
        }

        private async Task RedeployPodAsync()
        {
            if (SelectedPod == null) return;
            var dlg = new ConfirmDialog("Redeploy Pod", $"Trigger rollout redeployment for '{SelectedPod.Name}'?", confirmButtonText: "Redeploy");
            dlg.Owner = Application.Current.MainWindow;
            if (dlg.ShowDialog() == true && dlg.IsConfirmed)
            {
                IsBusy = true;
                BusyText = $"Redeploying '{SelectedPod.Name}'...";
                try
                {
                    bool ok = await _clusterService.RedeployPodAsync(SelectedPod);
                    if (ok)
                    {
                        await Task.Delay(2000);
                        await RefreshPodsAsync();
                    }
                    else
                    {
                        MessageBox.Show("Failed to redeploy pod.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                finally { IsBusy = false; }
            }
        }

        private async Task StopPodAsync()
        {
            if (SelectedPod == null) return;
            string controllerMsg = !string.IsNullOrEmpty(SelectedPod.ControlledBy)
                ? $"This pod is managed by {SelectedPod.ControlledBy}.\nStopping it will scale its Deployment to 0 replicas, shutting down all instances cleanly.\n\n(You can resume it anytime by scaling replicas back up in the Deployments tab)."
                : $"This is a standalone pod without a deployment controller.\nStopping it will terminate the pod.";

            var dlg = new ConfirmDialog(
                "Stop Pod (Scale to 0)",
                $"Are you sure you want to stop pod '{SelectedPod.Name}' in namespace '{SelectedPod.Namespace}'?\n\n{controllerMsg}",
                confirmButtonText: "⏹️ Stop Pod");
            dlg.Owner = Application.Current.MainWindow;

            if (dlg.ShowDialog() == true && dlg.IsConfirmed)
            {
                IsBusy = true;
                BusyText = $"Stopping pod '{SelectedPod.Name}'...";
                try
                {
                    bool ok = await _clusterService.StopPodAsync(SelectedPod);
                    if (ok)
                    {
                        await Task.Delay(1500);
                        await RefreshPodsAsync();
                        await RefreshDeploymentsAsync();
                    }
                    else
                    {
                        MessageBox.Show("Failed to stop pod.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        private async Task DeletePodAsync()
        {
            if (SelectedPod == null) return;
            var dlg = new ConfirmDialog("Delete Pod", $"Are you sure you want to delete pod '{SelectedPod.Name}' in namespace '{SelectedPod.Namespace}'?", showForce: true, confirmButtonText: "Delete");
            dlg.Owner = Application.Current.MainWindow;
            if (dlg.ShowDialog() == true && dlg.IsConfirmed)
            {
                IsBusy = true;
                BusyText = $"Deleting pod '{SelectedPod.Name}'...";
                try
                {
                    bool ok = await _clusterService.DeletePodAsync(SelectedPod.Name, SelectedPod.Namespace, dlg.IsForce);
                    if (ok)
                    {
                        await Task.Delay(1000);
                        await RefreshPodsAsync();
                    }
                    else
                    {
                        MessageBox.Show("Failed to delete pod.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                finally { IsBusy = false; }
            }
        }

        private void ExecInPod()
        {
            if (SelectedPod == null) return;
            var pod = SelectedPod;
            var dlg = new PodExecDialog(pod.Name, pod.Namespace, pod.Containers, (cnt, cmd) => _clusterService.ExecInPodAsync(pod.Name, pod.Namespace, cnt, cmd))
            {
                Owner = Application.Current.MainWindow
            };
            dlg.ShowDialog();
        }

        // Deployment Actions
        private async Task RefreshDeploymentsAsync()
        {
            if (!IsConnected) return;
            IsBusy = true;
            BusyText = "Refreshing Deployments...";
            try
            {
                var list = await _clusterService.GetDeploymentsAsync(SelectedNamespace);
                Deployments.Clear();
                foreach (var d in list) Deployments.Add(d);
                NotifyStatsChanged();
            }
            finally { IsBusy = false; }
        }

        private async Task ViewDeploymentYamlAsync()
        {
            if (SelectedDeployment == null) return;
            IsBusy = true;
            BusyText = $"Fetching deployment YAML for '{SelectedDeployment.Name}'...";
            try
            {
                string yaml = await _clusterService.GetDeploymentYamlAsync(SelectedDeployment.Name, SelectedDeployment.Namespace);
                var dlg = new YamlViewerDialog($"YAML - Deployment/{SelectedDeployment.Name}", yaml)
                {
                    Owner = Application.Current.MainWindow
                };
                dlg.ShowDialog();
            }
            finally { IsBusy = false; }
        }

        private async Task EditDeploymentYamlAsync()
        {
            if (SelectedDeployment == null) return;
            IsBusy = true;
            BusyText = $"Fetching deployment YAML for '{SelectedDeployment.Name}'...";
            try
            {
                string yaml = await _clusterService.GetDeploymentYamlAsync(SelectedDeployment.Name, SelectedDeployment.Namespace);
                var dlg = new YamlEditorDialog(SelectedDeployment.Name, SelectedDeployment.Namespace, yaml, _clusterService)
                {
                    Owner = Application.Current.MainWindow
                };
                if (dlg.ShowDialog() == true && dlg.WasApplied)
                {
                    await Task.Delay(1500);
                    await RefreshDeploymentsAsync();
                    await RefreshPodsAsync();
                }
            }
            finally { IsBusy = false; }
        }

        private async Task CopyDeploymentYamlAsync()
        {
            if (SelectedDeployment == null) return;
            var sfd = new SaveFileDialog
            {
                FileName = $"{SelectedDeployment.Name}-deployment.yaml",
                Filter = "YAML Files (*.yaml;*.yml)|*.yaml;*.yml|All Files (*.*)|*.*"
            };
            if (sfd.ShowDialog() == true)
            {
                IsBusy = true;
                BusyText = $"Saving deployment YAML for '{SelectedDeployment.Name}'...";
                try
                {
                    string yaml = await _clusterService.GetDeploymentYamlAsync(SelectedDeployment.Name, SelectedDeployment.Namespace);
                    await System.IO.File.WriteAllTextAsync(sfd.FileName, yaml);
                    MessageBox.Show($"Deployment YAML saved successfully to:\n{sfd.FileName}", "Saved to Windows", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save YAML: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally { IsBusy = false; }
            }
        }

        private async Task DescribeDeploymentAsync()
        {
            if (SelectedDeployment == null) return;
            IsBusy = true;
            BusyText = $"Describing deployment '{SelectedDeployment.Name}'...";
            try
            {
                string desc = await _clusterService.DescribeDeploymentAsync(SelectedDeployment.Name, SelectedDeployment.Namespace);
                var dlg = new YamlViewerDialog($"Describe - Deployment/{SelectedDeployment.Name}", desc)
                {
                    Owner = Application.Current.MainWindow
                };
                dlg.ShowDialog();
            }
            finally { IsBusy = false; }
        }

        private async Task ScaleDeploymentAsync()
        {
            if (SelectedDeployment == null) return;
            var dlg = new ScaleDialog(SelectedDeployment.Name, SelectedDeployment.Replicas)
            {
                Owner = Application.Current.MainWindow
            };
            if (dlg.ShowDialog() == true)
            {
                IsBusy = true;
                BusyText = $"Scaling deployment '{SelectedDeployment.Name}' to {dlg.Replicas} replicas...";
                try
                {
                    bool ok = await _clusterService.ScaleDeploymentAsync(SelectedDeployment.Name, SelectedDeployment.Namespace, dlg.Replicas);
                    if (ok)
                    {
                        await Task.Delay(1000);
                        await RefreshDeploymentsAsync();
                        await RefreshPodsAsync();
                    }
                    else
                    {
                        MessageBox.Show("Failed to scale deployment.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                finally { IsBusy = false; }
            }
        }

        private async Task RestartDeploymentAsync()
        {
            if (SelectedDeployment == null) return;
            var dlg = new ConfirmDialog("Rollout Restart Deployment", $"Trigger rollout restart for deployment '{SelectedDeployment.Name}'?", confirmButtonText: "Restart");
            dlg.Owner = Application.Current.MainWindow;
            if (dlg.ShowDialog() == true && dlg.IsConfirmed)
            {
                IsBusy = true;
                BusyText = $"Restarting deployment '{SelectedDeployment.Name}'...";
                try
                {
                    bool ok = await _clusterService.RestartDeploymentAsync(SelectedDeployment.Name, SelectedDeployment.Namespace);
                    if (ok)
                    {
                        await Task.Delay(1500);
                        await RefreshDeploymentsAsync();
                        await RefreshPodsAsync();
                    }
                }
                finally { IsBusy = false; }
            }
        }

        private async Task DeleteDeploymentAsync()
        {
            if (SelectedDeployment == null) return;
            var dlg = new ConfirmDialog("Delete Deployment", $"Are you sure you want to delete deployment '{SelectedDeployment.Name}' in namespace '{SelectedDeployment.Namespace}'?", confirmButtonText: "Delete");
            dlg.Owner = Application.Current.MainWindow;
            if (dlg.ShowDialog() == true && dlg.IsConfirmed)
            {
                IsBusy = true;
                BusyText = $"Deleting deployment '{SelectedDeployment.Name}'...";
                try
                {
                    bool ok = await _clusterService.DeleteDeploymentAsync(SelectedDeployment.Name, SelectedDeployment.Namespace);
                    if (ok)
                    {
                        await Task.Delay(1000);
                        await RefreshDeploymentsAsync();
                        await RefreshPodsAsync();
                    }
                }
                finally { IsBusy = false; }
            }
        }

        // PVC Actions
        private async Task RefreshPvcsAsync()
        {
            if (!IsConnected) return;
            IsBusy = true;
            BusyText = "Refreshing PVCs...";
            try
            {
                var list = await _clusterService.GetPvcsAsync(SelectedNamespace);
                Pvcs.Clear();
                foreach (var p in list) Pvcs.Add(p);
                NotifyStatsChanged();
            }
            finally { IsBusy = false; }
        }

        private async Task ViewPvcYamlAsync()
        {
            if (SelectedPvc == null) return;
            IsBusy = true;
            BusyText = $"Fetching PVC YAML for '{SelectedPvc.Name}'...";
            try
            {
                string yaml = await _clusterService.GetPvcYamlAsync(SelectedPvc.Name, SelectedPvc.Namespace);
                var dlg = new YamlViewerDialog($"YAML - PVC/{SelectedPvc.Name}", yaml)
                {
                    Owner = Application.Current.MainWindow
                };
                dlg.ShowDialog();
            }
            finally { IsBusy = false; }
        }

        private async Task CopyPvcYamlAsync()
        {
            if (SelectedPvc == null) return;
            var sfd = new SaveFileDialog
            {
                FileName = $"{SelectedPvc.Name}-pvc.yaml",
                Filter = "YAML Files (*.yaml;*.yml)|*.yaml;*.yml|All Files (*.*)|*.*"
            };
            if (sfd.ShowDialog() == true)
            {
                IsBusy = true;
                BusyText = $"Saving PVC YAML for '{SelectedPvc.Name}'...";
                try
                {
                    string yaml = await _clusterService.GetPvcYamlAsync(SelectedPvc.Name, SelectedPvc.Namespace);
                    await System.IO.File.WriteAllTextAsync(sfd.FileName, yaml);
                    MessageBox.Show($"PVC YAML saved successfully to:\n{sfd.FileName}", "Saved to Windows", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save YAML: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally { IsBusy = false; }
            }
        }

        private void BrowsePvcFiles()
        {
            if (SelectedPvc == null) return;
            var dlg = new PvcExplorerDialog(SelectedPvc, _clusterService)
            {
                Owner = Application.Current.MainWindow
            };
            dlg.ShowDialog();
        }

        private async Task DescribePvcAsync()
        {
            if (SelectedPvc == null) return;
            IsBusy = true;
            BusyText = $"Describing PVC '{SelectedPvc.Name}'...";
            try
            {
                string desc = await _clusterService.DescribePvcAsync(SelectedPvc.Name, SelectedPvc.Namespace);
                var dlg = new YamlViewerDialog($"Describe - PVC/{SelectedPvc.Name}", desc)
                {
                    Owner = Application.Current.MainWindow
                };
                dlg.ShowDialog();
            }
            finally { IsBusy = false; }
        }

        private async Task DeletePvcAsync()
        {
            if (SelectedPvc == null) return;
            var dlg = new ConfirmDialog("Delete PVC", $"Are you sure you want to delete PVC '{SelectedPvc.Name}' in namespace '{SelectedPvc.Namespace}'? Persistent data might be lost!", confirmButtonText: "Delete PVC");
            dlg.Owner = Application.Current.MainWindow;
            if (dlg.ShowDialog() == true && dlg.IsConfirmed)
            {
                IsBusy = true;
                BusyText = $"Deleting PVC '{SelectedPvc.Name}'...";
                try
                {
                    bool ok = await _clusterService.DeletePvcAsync(SelectedPvc.Name, SelectedPvc.Namespace);
                    if (ok)
                    {
                        await Task.Delay(1000);
                        await RefreshPvcsAsync();
                    }
                }
                finally { IsBusy = false; }
            }
        }

        // Secret Actions
        private async Task RefreshSecretsAsync()
        {
            if (!IsConnected) return;
            IsBusy = true;
            BusyText = "Refreshing Secrets...";
            try
            {
                var list = await _clusterService.GetSecretsAsync(SelectedNamespace);
                Secrets.Clear();
                foreach (var s in list) Secrets.Add(s);
                NotifyStatsChanged();
            }
            finally { IsBusy = false; }
        }

        private void ViewSecretData()
        {
            if (SelectedSecret == null) return;
            var dlg = new SecretViewerDialog(SelectedSecret, _clusterService)
            {
                Owner = Application.Current.MainWindow
            };
            dlg.ShowDialog();
        }

        private async Task ViewSecretYamlAsync()
        {
            if (SelectedSecret == null) return;
            IsBusy = true;
            BusyText = $"Fetching secret YAML for '{SelectedSecret.Name}'...";
            try
            {
                string yaml = await _clusterService.GetSecretYamlAsync(SelectedSecret.Name, SelectedSecret.Namespace);
                var dlg = new YamlViewerDialog($"Secret YAML - {SelectedSecret.Namespace}/{SelectedSecret.Name}", yaml)
                {
                    Owner = Application.Current.MainWindow
                };
                dlg.ShowDialog();
            }
            finally { IsBusy = false; }
        }

        private async Task EditSecretYamlAsync()
        {
            if (SelectedSecret == null) return;
            IsBusy = true;
            BusyText = $"Fetching secret YAML for '{SelectedSecret.Name}'...";
            try
            {
                string yaml = await _clusterService.GetSecretYamlAsync(SelectedSecret.Name, SelectedSecret.Namespace);
                var dlg = new YamlEditorDialog($"Secret/{SelectedSecret.Name}", SelectedSecret.Namespace, yaml, _clusterService)
                {
                    Owner = Application.Current.MainWindow
                };
                if (dlg.ShowDialog() == true && dlg.WasApplied)
                {
                    await Task.Delay(1000);
                    await RefreshSecretsAsync();
                }
            }
            finally { IsBusy = false; }
        }

        private async Task CopySecretYamlAsync()
        {
            if (SelectedSecret == null) return;
            var sfd = new SaveFileDialog
            {
                FileName = $"{SelectedSecret.Name}-secret.yaml",
                Filter = "YAML Files (*.yaml;*.yml)|*.yaml;*.yml|All Files (*.*)|*.*"
            };
            if (sfd.ShowDialog() == true)
            {
                IsBusy = true;
                BusyText = $"Saving secret YAML for '{SelectedSecret.Name}'...";
                try
                {
                    string yaml = await _clusterService.GetSecretYamlAsync(SelectedSecret.Name, SelectedSecret.Namespace);
                    await System.IO.File.WriteAllTextAsync(sfd.FileName, yaml);
                    MessageBox.Show($"Secret YAML saved successfully to:\n{sfd.FileName}", "Saved to Windows", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save YAML: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally { IsBusy = false; }
            }
        }

        private async Task DescribeSecretAsync()
        {
            if (SelectedSecret == null) return;
            IsBusy = true;
            BusyText = $"Describing secret '{SelectedSecret.Name}'...";
            try
            {
                string desc = await _clusterService.DescribeSecretAsync(SelectedSecret.Name, SelectedSecret.Namespace);
                var dlg = new YamlViewerDialog($"Describe Secret - {SelectedSecret.Namespace}/{SelectedSecret.Name}", desc)
                {
                    Owner = Application.Current.MainWindow
                };
                dlg.ShowDialog();
            }
            finally { IsBusy = false; }
        }

        private async Task DeleteSecretAsync()
        {
            if (SelectedSecret == null) return;
            var dlg = new ConfirmDialog("Delete Secret", $"Are you sure you want to delete secret '{SelectedSecret.Name}' in namespace '{SelectedSecret.Namespace}'?", confirmButtonText: "Delete Secret");
            dlg.Owner = Application.Current.MainWindow;
            if (dlg.ShowDialog() == true && dlg.IsConfirmed)
            {
                IsBusy = true;
                BusyText = $"Deleting secret '{SelectedSecret.Name}'...";
                try
                {
                    bool ok = await _clusterService.DeleteSecretAsync(SelectedSecret.Name, SelectedSecret.Namespace);
                    if (ok)
                    {
                        await Task.Delay(1000);
                        await RefreshSecretsAsync();
                    }
                    else
                    {
                        MessageBox.Show("Failed to delete secret.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                finally { IsBusy = false; }
            }
        }

        // Ingress & OpenShift Routes
        private async Task RefreshIngressRoutesAsync()
        {
            if (!IsConnected) return;
            IsBusy = true;
            BusyText = "Fetching Ingress and Routes...";
            try
            {
                var list = await _clusterService.GetIngressRoutesAsync(SelectedNamespace);
                IngressRoutes.Clear();
                foreach (var item in list) IngressRoutes.Add(item);
                OnPropertyChanged(nameof(TotalRoutesCount));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to fetch Ingress/Routes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RefreshRouterStatusAsync()
        {
            if (!IsConnected) return;
            try
            {
                RouterStatus = await _clusterService.GetRouterStatusAsync();
            }
            catch { }
        }

        private void OpenRouteInBrowser()
        {
            if (SelectedIngressRoute == null || string.IsNullOrWhiteSpace(SelectedIngressRoute.FullUrl)) return;
            try
            {
                Process.Start(new ProcessStartInfo(SelectedIngressRoute.FullUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open URL: {ex.Message}", "Browser Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task EditIngressRouteYamlAsync()
        {
            if (SelectedIngressRoute == null) return;
            IsBusy = true;
            BusyText = $"Fetching YAML for {SelectedIngressRoute.Kind} '{SelectedIngressRoute.Name}'...";
            try
            {
                string yaml = await _clusterService.GetIngressRouteYamlAsync(SelectedIngressRoute.Name, SelectedIngressRoute.Namespace, SelectedIngressRoute.Kind);
                var editor = new YamlEditorDialog($"{SelectedIngressRoute.Kind}: {SelectedIngressRoute.Name}", SelectedIngressRoute.Namespace, yaml, _clusterService)
                {
                    Owner = Application.Current.MainWindow
                };
                if (editor.ShowDialog() == true)
                {
                    await RefreshIngressRoutesAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load YAML: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task DescribeIngressRouteAsync()
        {
            if (SelectedIngressRoute == null) return;
            IsBusy = true;
            BusyText = $"Describing {SelectedIngressRoute.Kind} '{SelectedIngressRoute.Name}'...";
            try
            {
                string desc = await _clusterService.DescribeIngressRouteAsync(SelectedIngressRoute.Name, SelectedIngressRoute.Namespace, SelectedIngressRoute.Kind);
                var viewer = new FileViewerDialog($"Describe: {SelectedIngressRoute.Name}", desc)
                {
                    Owner = Application.Current.MainWindow
                };
                viewer.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Describe failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task DeleteIngressRouteAsync()
        {
            if (SelectedIngressRoute == null) return;
            var confirm = MessageBox.Show(
                $"Are you sure you want to delete {SelectedIngressRoute.Kind} '{SelectedIngressRoute.Name}' in namespace '{SelectedIngressRoute.Namespace}'?",
                $"Delete {SelectedIngressRoute.Kind}",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            IsBusy = true;
            BusyText = $"Deleting {SelectedIngressRoute.Kind} '{SelectedIngressRoute.Name}'...";
            try
            {
                bool ok = await _clusterService.DeleteIngressRouteAsync(SelectedIngressRoute.Name, SelectedIngressRoute.Namespace, SelectedIngressRoute.Kind);
                if (ok)
                {
                    IngressRoutes.Remove(SelectedIngressRoute);
                    OnPropertyChanged(nameof(TotalRoutesCount));
                }
                else
                {
                    MessageBox.Show($"Failed to delete {SelectedIngressRoute.Kind}.", "Delete Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error deleting {SelectedIngressRoute.Kind}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        // Containers & Docker
        private async Task RefreshContainersAsync()
        {
            if (!IsConnected) return;
            IsBusy = true;
            BusyText = "Refreshing running Docker / Podman containers...";
            try
            {
                var list = await _clusterService.GetContainersAsync(true);
                Containers.Clear();
                foreach (var c in list) Containers.Add(c);
                NotifyStatsChanged();
            }
            finally { IsBusy = false; }
        }

        private async Task ViewContainerLogsAsync()
        {
            if (SelectedContainer == null) return;
            var c = SelectedContainer;
            var dlg = new LogViewerDialog(
                $"Container Logs: {c.Name} ({c.ShortId})",
                new List<string> { c.Name },
                (cnt, lines, prev) => _clusterService.GetContainerLogsAsync(c.Id, lines)
            )
            {
                Owner = Application.Current.MainWindow
            };
            dlg.ShowDialog();
            await Task.CompletedTask;
        }

        private async Task StartContainerAsync()
        {
            if (SelectedContainer == null) return;
            IsBusy = true;
            BusyText = $"Starting container '{SelectedContainer.ShortId}'...";
            try
            {
                await _clusterService.StartContainerAsync(SelectedContainer.Id);
                await RefreshContainersAsync();
            }
            finally { IsBusy = false; }
        }

        private async Task StopContainerAsync()
        {
            if (SelectedContainer == null) return;
            IsBusy = true;
            BusyText = $"Stopping container '{SelectedContainer.ShortId}'...";
            try
            {
                await _clusterService.StopContainerAsync(SelectedContainer.Id);
                await RefreshContainersAsync();
            }
            finally { IsBusy = false; }
        }

        private async Task RestartContainerAsync()
        {
            if (SelectedContainer == null) return;
            IsBusy = true;
            BusyText = $"Restarting container '{SelectedContainer.ShortId}'...";
            try
            {
                await _clusterService.RestartContainerAsync(SelectedContainer.Id);
                await RefreshContainersAsync();
            }
            finally { IsBusy = false; }
        }

        private async Task RemoveContainerAsync()
        {
            if (SelectedContainer == null) return;
            var dlg = new ConfirmDialog("Remove Container", $"Are you sure you want to remove container '{SelectedContainer.Name}' ({SelectedContainer.ShortId})?", confirmButtonText: "Remove");
            dlg.Owner = Application.Current.MainWindow;
            if (dlg.ShowDialog() == true && dlg.IsConfirmed)
            {
                IsBusy = true;
                BusyText = $"Removing container '{SelectedContainer.ShortId}'...";
                try
                {
                    await _clusterService.RemoveContainerAsync(SelectedContainer.Id, true);
                    await RefreshContainersAsync();
                }
                finally { IsBusy = false; }
            }
        }

        // Docker / Podman Images
        private async Task RefreshImagesAsync()
        {
            if (!IsConnected) return;
            IsBusy = true;
            BusyText = "Refreshing Docker / Podman images...";
            try
            {
                var list = await _clusterService.GetImagesAsync();
                Images.Clear();
                foreach (var img in list) Images.Add(img);
                NotifyStatsChanged();
            }
            finally { IsBusy = false; }
        }

        private async Task DeleteImageAsync()
        {
            if (SelectedImage == null) return;
            var dlg = new ConfirmDialog("Delete Image", $"Are you sure you want to delete image '{SelectedImage.FullName}'?", showForce: true, confirmButtonText: "Delete Image");
            dlg.Owner = Application.Current.MainWindow;
            if (dlg.ShowDialog() == true && dlg.IsConfirmed)
            {
                IsBusy = true;
                BusyText = $"Deleting image '{SelectedImage.FullName}'...";
                try
                {
                    bool ok = await _clusterService.DeleteImageAsync(SelectedImage.Id, dlg.IsForce);
                    if (ok)
                    {
                        await RefreshImagesAsync();
                    }
                    else
                    {
                        MessageBox.Show("Failed to delete image. It may be used by a running container. Try Force delete.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                finally { IsBusy = false; }
            }
        }

        private async Task PullImageAsync()
        {
            var dlg = new PullImageDialog { Owner = Application.Current.MainWindow };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ImageName))
            {
                IsBusy = true;
                BusyText = $"Pulling image '{dlg.ImageName}' (this might take a minute)...";
                try
                {
                    bool ok = await _clusterService.PullImageAsync(dlg.ImageName);
                    if (ok)
                    {
                        MessageBox.Show($"Image '{dlg.ImageName}' pulled successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        await RefreshImagesAsync();
                    }
                    else
                    {
                        MessageBox.Show($"Failed to pull image '{dlg.ImageName}'. Check command logs for details.", "Pull Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                finally { IsBusy = false; }
            }
        }

        // Remote Files Explorer
        private async Task RefreshFilesAsync()
        {
            if (!IsConnected) return;
            IsBusy = true;
            BusyText = $"Listing files in '{CurrentRemotePath}'...";
            try
            {
                var files = await _sshService.ListDirectoryAsync(CurrentRemotePath);
                RemoteFiles.Clear();
                foreach (var f in files) RemoteFiles.Add(f);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not list directory: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { IsBusy = false; }
        }

        private async Task OpenRemoteItemAsync(object? param)
        {
            var item = param as RemoteFileInfo ?? SelectedRemoteFile;
            if (item == null) return;

            if (item.IsDirectory)
            {
                CurrentRemotePath = item.FullPath;
                await RefreshFilesAsync();
            }
            else
            {
                IsBusy = true;
                BusyText = $"Reading file '{item.Name}'...";
                try
                {
                    string content = await _sshService.ReadTextFileAsync(item.FullPath);
                    var dlg = new FileViewerDialog(item.FullPath, content)
                    {
                        Owner = Application.Current.MainWindow
                    };
                    dlg.ShowDialog();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not read file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                finally { IsBusy = false; }
            }
        }

        private async Task GoUpDirectoryAsync()
        {
            if (string.IsNullOrEmpty(CurrentRemotePath) || CurrentRemotePath == "/") return;
            int idx = CurrentRemotePath.TrimEnd('/').LastIndexOf('/');
            CurrentRemotePath = idx <= 0 ? "/" : CurrentRemotePath.Substring(0, idx);
            await RefreshFilesAsync();
        }

        private async Task DownloadRemoteFileAsync()
        {
            if (SelectedRemoteFile == null || SelectedRemoteFile.IsDirectory) return;
            var dlg = new SaveFileDialog
            {
                FileName = SelectedRemoteFile.Name,
                Filter = "All Files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                IsBusy = true;
                BusyText = $"Downloading {SelectedRemoteFile.Name}...";
                try
                {
                    await _sshService.DownloadFileAsync(SelectedRemoteFile.FullPath, dlg.FileName);
                    MessageBox.Show("File downloaded successfully!", "Downloaded", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Download failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally { IsBusy = false; }
            }
        }

        private async Task UploadRemoteFileAsync()
        {
            if (!IsConnected) return;
            var ofd = new OpenFileDialog
            {
                Title = $"Select file to upload to {CurrentRemotePath}",
                Filter = "All Files (*.*)|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                string localFile = ofd.FileName;
                string remotePath = CurrentRemotePath.TrimEnd('/') + "/" + System.IO.Path.GetFileName(localFile);
                IsBusy = true;
                BusyText = $"Uploading {System.IO.Path.GetFileName(localFile)} to {remotePath}...";
                try
                {
                    await _sshService.UploadFileAsync(localFile, remotePath);
                    MessageBox.Show($"File uploaded successfully to:\n{remotePath}", "Upload Completed", MessageBoxButton.OK, MessageBoxImage.Information);
                    await RefreshFilesAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Upload failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally { IsBusy = false; }
            }
        }

        // Custom Behind-the-Scenes Command runner
        private async Task ExecuteCustomCommandAsync()
        {
            if (!IsConnected || string.IsNullOrWhiteSpace(CustomCommandText)) return;
            string cmd = CustomCommandText.Trim();
            IsBusy = true;
            BusyText = $"Executing: {cmd}...";
            try
            {
                await _sshService.RunCommandAsync(cmd);
                IsCommandLogVisible = true; // unfold so user sees result
            }
            finally { IsBusy = false; }
        }

        private void UpdateAutoRefreshTimer()
        {
            _autoRefreshTimer.Stop();
            switch (SelectedRefreshInterval)
            {
                case "10s":
                    _autoRefreshTimer.Interval = TimeSpan.FromSeconds(10);
                    _autoRefreshTimer.Start();
                    break;
                case "30s":
                    _autoRefreshTimer.Interval = TimeSpan.FromSeconds(30);
                    _autoRefreshTimer.Start();
                    break;
                case "60s":
                    _autoRefreshTimer.Interval = TimeSpan.FromSeconds(60);
                    _autoRefreshTimer.Start();
                    break;
                default:
                    break;
            }
        }
    }
}
