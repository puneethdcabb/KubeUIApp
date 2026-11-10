# KubeShift Manager — Technical Architecture & Design Document

> **Target Audience**: AI Agents, System Architects, and Software Engineers continuing development or adding features to this codebase.

---

## 1. System Overview & Vision

**KubeShift Manager** is a desktop client built with **.NET 10** and **WPF** that manages remote Linux nodes running **Red Hat OpenShift**, **MicroShift**, and standard **Kubernetes** distributions, as well as standalone container runtimes (**Podman**, **Docker**, **CRI-O / crictl**).

### Core Design Philosophy: Zero-Agent Invisible SSH Architecture
Unlike traditional Kubernetes dashboards that require exposing `kube-apiserver` to public networks or installing in-cluster NodePort/Ingress dashboard agents (like Kubernetes Dashboard or Lens agents), **KubeShift Manager connects purely over a standard SSH channel (Port 22)**:
1. **Zero Host Alterations**: No Daemons, agents, or open web ports required on the target Linux node.
2. **Invisible Backend Execution**: All commands are executed silently behind the scenes. The command window is never shown to the user.
3. **Structured UI Visualizer**: Raw terminal commands (`oc get ... -o json`, `crictl ps -o json`, `podman images --format json`) are parsed in-memory and transformed into high-fidelity, interactive desktop controls (sortable DataGrids, color-coded badges, log tail viewers, YAML code viewers, and dialog controls).
4. **Behind-the-Scenes Transparency**: A collapsible diagnostic drawer allows developers and operators to inspect the exact SSH commands executed, execution duration in milliseconds, exit codes, and raw output streams.

---

## 2. Solution Structure & Code Map

```
c:\Users\punee\Documents\KubeUIApp\
│
├── App.xaml / App.xaml.cs                 # Application bootstrap, Global DynamicResource Brushes,
│                                          # High-DPI TextBox/PasswordBox/ComboBox/DataGrid styles.
├── MainWindow.xaml / MainWindow.xaml.cs   # Main Dashboard: Connection Bar, Telemetry Ribbon,
│                                          # Multi-Tab Workload Navigator, and Activity Drawer.
├── KubeUIApp.csproj                       # Target: net10.0-windows, UseWPF=true, SSH.NET 2026.0.0
├── .gitignore                             # Clean repository exclusions (bin, obj, publish, vs)
│
├── Converters/
│   └── ValueConverters.cs                 # InverseBooleanConverter, InverseBoolToVisibilityConverter,
│                                          # NullToVisibilityConverter.
├── Models/
│   ├── ConnectionProfile.cs               # Host, Port, Auth credentials, Sudo, Custom Kubeconfig.
│   ├── PodItem.cs                         # Pod identity, status badge color, containers, restarts, age.
│   ├── DeploymentItem.cs                  # Replicas, images, available/updated counts, age.
│   ├── PvcItem.cs                         # Persistent Volume Claims: volume, capacity, access modes.
│   ├── ContainerItem.cs                   # Container runtime: podman/docker/crictl container state.
│   ├── DockerImageItem.cs                 # Image repository, tag, short ID, virtual size.
│   ├── RemoteFileInfo.cs                  # Remote SFTP file browser metadata (name, size, permissions).
│   ├── NodeStats.cs                       # Linux OS, Kernel, Uptime, CPU load, Memory, Disk, Versions.
│   └── CommandLogEntry.cs                 # Behind-the-scenes SSH command audit trail record.
│
├── Services/
│   ├── ISshService.cs / SshService.cs     # Encrypted SSH execution & SFTP file transfer engine.
│   ├── IClusterService.cs                 # High-level Kubernetes & Container abstraction interface.
│   ├── ClusterService.cs                  # Command orchestrator, CLI auto-detector, JSON parser.
│   ├── ProfileStorageService.cs           # Local profile persistence in %APPDATA%\KubeUIApp.
│   └── ThemeService.cs                    # Dynamic Resource theme engine (Dark / Light switcher).
│
├── ViewModels/
│   ├── ViewModelBase.cs                   # INotifyPropertyChanged implementation with SetField.
│   ├── RelayCommand.cs                    # Synchronous & Asynchronous ICommand implementations.
│   └── MainViewModel.cs                   # Central coordinator: reactive state, search filters,
│                                          # background timers, workload collections.
└── Views/
    ├── ConfirmDialog.xaml (.cs)           # Guarded deletion dialog with force-delete toggle.
    ├── ScaleDialog.xaml (.cs)             # Deployment replica scaling dialog with +/- steppers.
    ├── PullImageDialog.xaml (.cs)         # Container image pulling dialog.
    ├── LogViewerDialog.xaml (.cs)         # Multi-container log viewer with auto-tail & filter.
    ├── YamlViewerDialog.xaml (.cs)        # Monospace YAML & Describe resource inspector with copy to Windows.
    ├── YamlEditorDialog.xaml (.cs)        # Live interactive YAML editor with cluster Apply & Redeploy workflow.
    ├── PodExecDialog.xaml (.cs)           # Container command runner (interactive shell exec).
    ├── PvcExplorerDialog.xaml (.cs)       # PVC file explorer: browse, view, copy to Windows, upload to PVC.
    ├── PodFileExplorerDialog.xaml (.cs)   # Pod file explorer: browse container files, copy to Windows, upload.
    ├── SecretViewerDialog.xaml (.cs)      # Secret & Certificate/CRL data inspector (Base64 vs Decoded).
    └── FileViewerDialog.xaml (.cs)        # Remote file reader & local Windows file exporter.
```

---

## 3. Core Architectural Components

### 3.1. SSH Service (`SshService.cs`)
- **Connection Management**: Instantiates `Renci.SshNet.SshClient` and `SftpClient` using either Password or Private Key authentication.
- **Concurrency Guard**: Uses `SemaphoreSlim _lock = new(1, 1)` to prevent race conditions during connection/disconnection.
- **Universal Sudo Privilege Elevation**: All backend commands execute with root administrative privileges by default (`UseSudo = true`):
  - Automatically wrapped inside `bash -c '<escapedCmd>'` to properly execute bash builtins (`export`), pipes, and redirection under `sudo`.
  - For non-root SSH accounts: piped via `echo '<password>' | sudo -S -p '' bash -c '<escapedCmd>'` or `sudo bash -c '<escapedCmd>'`.
  - For `root` user logins: executed directly via `bash -c '<escapedCmd>'`.
- **Root-Privilege File Transfer**: SFTP standard permissions can be blocked when files require root-only access (e.g. `/var/lib/microshift`). `SshService` implements fallback base64 file transfer over the privileged SSH shell channel (`base64` encode/decode), guaranteeing reliable file uploads and downloads regardless of SFTP filesystem permission flags.
- **Stream Processing**: Uses asynchronous streams (`StreamReader`) for both `OutputStream` (stdout) and `ExtendedOutputStream` (stderr) with millisecond-precision stopwatch timing.
- **Logging Event**: Emits `CommandExecuted?.Invoke(CommandLogEntry)` so the UI drawer updates reactively without polling.

### 3.2. Cluster & Workload Orchestrator (`ClusterService.cs`)
- **Auto-Discovery Workflow (`InitializeAsync`)**:
  1. **KUBECONFIG Discovery**:
     - Checks if user provided custom path.
     - Probes MicroShift standard: `/var/lib/microshift/resources/kubeadmin/kubeconfig`.
     - Probes Vanilla K8s standard: `/etc/kubernetes/admin.conf`.
     - Probes default `~/.kube/config`.
  2. **CLI Discovery**:
     - Tests `oc version --client` (OpenShift / MicroShift preferred).
     - Falls back to `kubectl version --client`.
  3. **Container Runtime Discovery**:
     - Probes `podman --version` (Red Hat default).
     - Probes `crictl --version` (Kubernetes CRI).
     - Probes `docker --version`.
- **Command Dispatcher (`BuildKubeCmd`)**:
  Automatically prefixes all commands with the detected kubeconfig environment variable:
  ```bash
  export KUBECONFIG='/var/lib/microshift/resources/kubeadmin/kubeconfig'; oc <subcommand>
  ```
- **PVC File Access Engine (`ListPvcFilesAsync`, `CopyPvcFileToWindowsAsync`, `CopyWindowsFileToPvcAsync`)**:
  Accesses Persistent Volume Claims using a two-tier strategy:
  1. **Tier 1 (Active Pod Mount)**: Inspects pods mounting the PVC (`oc get pods -o json` inspecting `.spec.volumes` and `.volumeMounts`). If a running pod mounts the volume, executes directory listing and base64 streaming through `oc exec <pod> -c <container> -- ls -la /mountPath`.
  2. **Tier 2 (Node Storage Direct)**: If no running pod is mounted, resolves the PV claim (`oc get pvc <name> -o json` -> `.spec.volumeName`) and inspects the backing host path, TopoLVM path, or MicroShift storage directory (`/var/lib/microshift/data/...`, `/var/lib/containers/storage/volumes/...`).
- **Pod File Explorer Engine (`ListPodFilesDetailedAsync`, `CopyPodFileToWindowsAsync`, `CopyWindowsFileToPodAsync`)**:
  Navigates container filesystems in real-time via `oc exec <pod> -c <container> -- ls -la --time-style=+%Y-%m-%d\ %H:%M:%S <path>`. Streams file bytes to/from Windows via chunked base64 pipelines.
- **Secrets & Certificate/CRL Inspection Engine (`GetSecretsAsync`, `GetSecretDataEntriesAsync`)**:
  - Fetches secrets via `oc get secrets {nsFlag} -o json` and deserializes metadata, type, and data key collections.
  - In `GetSecretDataEntriesAsync`, extracts `.data` properties, decodes base64 bytes, and performs content heuristic analysis to categorize entries into X.509 Certificates, Certificate Revocation Lists (CRLs), Private Keys, Tokens, and JSON documents.
  - The UI presents both the raw stored Base64 string and decoded UTF-8 text with one-click local file export.

- **High-Performance JSON Parsing**:
  Executes commands with `-o json` and parses output using `System.Text.Json.JsonDocument` to extract metadata, labels, container statuses, ownerReferences, and phase reasons with zero regex fragility.

### 3.3. Dynamic Theme Engine (`ThemeService.cs`)
All UI elements bind to brush resources using `DynamicResource` instead of `StaticResource`.
When switching themes, `ThemeService.ApplyTheme(bool isDark)` mutates the dictionary in `Application.Current.Resources`:
- **Dark Palette**: Catppuccin Mocha / Tokyo Dark inspired (`#1E1E2E` base, `#181825` surface, `#252538` cards, `#89B4FA` sapphire accent).
- **Light Palette**: Modern Enterprise / GitHub Light (`#F3F4F6` base, `#FFFFFF` surface, `#E5E7EB` cards, `#2563EB` blue accent).

---

## 4. Crucial UI & WPF Implementation Details

### 4.1. Text Box & Input Field Formatting
> **Important Rule for Future UI Additions**:
> WPF's default `TextBox` control template uses `<ScrollViewer x:Name="PART_ContentHost"/>`.
> - **DO NOT** place a `Margin` on `PART_ContentHost` (this shifts the baseline downward and causes the bottom half of the font glyphs to be clipped in fixed-height boxes).
> - **DO** apply `Padding="{TemplateBinding Padding}"` and `VerticalAlignment="{TemplateBinding VerticalContentAlignment}"` directly to `PART_ContentHost`.
> - Always specify `HorizontalScrollBarVisibility="Hidden"` and `VerticalScrollBarVisibility="Hidden"`.

### 4.2. Reactive Filtering Pattern
Each workload collection in `MainViewModel.cs` exposes both an `ObservableCollection<T>` and an `ICollectionView`:
```csharp
FilteredPods = CollectionViewSource.GetDefaultView(Pods);
FilteredPods.Filter = obj => {
    if (obj is not PodItem p) return false;
    if (string.IsNullOrWhiteSpace(_podSearchText)) return true;
    return p.Name.Contains(_podSearchText, StringComparison.OrdinalIgnoreCase) ||
           p.Namespace.Contains(_podSearchText, StringComparison.OrdinalIgnoreCase);
};
```
When `PodSearchText` changes, `FilteredPods.Refresh()` is invoked, providing instant 60 FPS filtering without re-querying the remote cluster.

---

## 5. Developer Guide & Extension Roadmap

### 5.1. Adding a New Workload Tab (e.g. Services / Routes)
To add a new resource type (e.g., Kubernetes `Service` or OpenShift `Route`):
1. **Create Model**: Add `Models/ServiceItem.cs` (Name, Namespace, Type: ClusterIP/NodePort/LoadBalancer, ClusterIP, ExternalIP, Ports, Age).
2. **Add Service Methods**:
   - In `IClusterService.cs`: Add `Task<List<ServiceItem>> GetServicesAsync(string? ns = null)`.
   - In `ClusterService.cs`: Execute `BuildKubeCmd($"get services {nsFlag} -o json")` and parse array.
3. **Update ViewModel**:
   - In `MainViewModel.cs`: Add `ObservableCollection<ServiceItem> Services`, `FilteredServices`, `RefreshServicesCommand`.
4. **Add UI Tab**:
   - In `MainWindow.xaml`: Add a `<TabItem Header="🌐 Services">` containing a search bar and `DataGrid` styled with `{DynamicResource ...}` brushes.

### 5.2. Adding Interactive PTY Terminal for Pod Exec
Currently `PodExecDialog` runs commands asynchronously via `exec ... -- <cmd>` and captures stdout.
To add an interactive PTY session (e.g. `bash` shell with live typing):
- Use `_sshClient.CreateShellStream("xterm", 80, 24, 800, 600, 1024)`.
- Connect the stream to a WPF terminal control or custom RichTextBox with ANSI color parsing.

### 5.3. Recommended Testing Strategy
- **Unit Testing**: Create mock implementations of `ISshService` returning predefined JSON strings from real clusters (e.g. `oc get pods -A -o json`).
- **Integration Testing**: Test against local MicroShift instances running in a VM (Hyper-V, Libvirt, WSL2, or Vagrant).

---

## 6. Build & Deployment Commands

```powershell
# Restore dependencies
dotnet restore

# Build Debug
dotnet build

# Build Release
dotnet build -c Release

# Publish Standalone Win-x64 Executable
dotnet publish -c Release -r win-x64 --self-contained false -o ./publish
```
