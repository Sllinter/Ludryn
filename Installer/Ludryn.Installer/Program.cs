using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace Ludryn.Installer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerForm());
    }
}

internal sealed class InstallerForm : Form
{
    private const string FseToolUrl =
        "https://github.com/ashpynov/XboxFullScreenExperienceTool/releases/latest";

    private readonly Label _title = new()
    {
        AutoSize = false,
        Font = new Font("Segoe UI", 20, FontStyle.Bold),
        ForeColor = Color.White,
        Text = "Instalando Ludryn",
        TextAlign = ContentAlignment.MiddleLeft
    };

    private readonly Label _status = new()
    {
        AutoSize = false,
        Font = new Font("Segoe UI", 10),
        ForeColor = Color.FromArgb(205, 205, 215),
        Text = "Preparando o Xbox Home App...",
        TextAlign = ContentAlignment.MiddleLeft
    };

    private readonly ProgressBar _progress = new()
    {
        Style = ProgressBarStyle.Marquee,
        MarqueeAnimationSpeed = 24
    };

    public InstallerForm()
    {
        Text = "Ludryn Setup";
        BackColor = Color.FromArgb(13, 13, 17);
        ClientSize = new Size(560, 220);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        _title.SetBounds(34, 30, 490, 50);
        _status.SetBounds(36, 91, 488, 54);
        _progress.SetBounds(36, 158, 488, 18);
        Controls.AddRange([_title, _status, _progress]);

        Shown += async (_, _) =>
        {
            var prerequisitesConfirmed = MessageBox.Show(
                this,
                "Antes de instalar o Ludryn:\n\n" +
                "1. Atualize o Windows 11.\n" +
                "2. Execute o Xbox Full Screen Experience Tool para habilitar o FSE/Handheld Mode.\n" +
                "3. Depois, instale o Ludryn e selecione-o como Xbox Home App.\n\n" +
                "O FSE ja esta habilitado neste computador?",
                "Pre-requisitos do Ludryn",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (prerequisitesConfirmed != DialogResult.Yes)
            {
                Process.Start(new ProcessStartInfo(FseToolUrl)
                {
                    UseShellExecute = true
                });
                Close();
                return;
            }

            await InstallAsync();
        };
        FormClosing += (_, e) =>
        {
            if (_progress.Style == ProgressBarStyle.Marquee)
            {
                e.Cancel = true;
            }
        };
    }

    private async Task InstallAsync()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"LudrynSetup-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(temporaryDirectory);

            SetStatus("Extraindo os componentes do Ludryn...");
            var certificatePath = ExtractResource(
                "Ludryn.Payload.Ludryn.cer",
                Path.Combine(temporaryDirectory, "Ludryn.cer"));
            var runtimePath = ExtractResource(
                "Ludryn.Payload.WindowsAppRuntime.msix",
                Path.Combine(temporaryDirectory, "WindowsAppRuntime.msix"));
            var packagePath = ExtractResource(
                "Ludryn.Payload.Ludryn.msix",
                Path.Combine(temporaryDirectory, "Ludryn.msix"));

            SetStatus("Registrando a assinatura segura do aplicativo...");
            await Task.Run(() => TrustCertificate(certificatePath));

            SetStatus("Preparando os componentes do Windows...");
            var runtimeCommand = $$"""
                $requiredVersion = [version]'2.1.3.0';
                $installedRuntime = Get-AppxPackage -Name 'Microsoft.WindowsAppRuntime.2' |
                    Where-Object { $_.Architecture -eq 'X64' } |
                    Sort-Object { [version]$_.Version } -Descending |
                    Select-Object -First 1;
                if (-not $installedRuntime -or [version]$installedRuntime.Version -lt $requiredVersion) {
                    Add-AppxPackage -Path {{Quote(runtimePath)}} -ForceUpdateFromAnyVersion
                }
                """;
            await RunPowerShellAsync(runtimeCommand);

            SetStatus("Instalando o Ludryn como Xbox Home App...");
            var dataBackupPath = Path.Combine(temporaryDirectory, "UserDataBackup");
            var installCommand = $$"""
                $backupRoot = {{Quote(dataBackupPath)}};
                $externalData = Join-Path $env:LOCALAPPDATA 'Ludryn';
                $packageLocalState = Join-Path $env:LOCALAPPDATA 'Packages\Kauac.Ludryn_d9yz5w7yexjqp\LocalState';

                function Copy-LudrynDirectory($source, $destination) {
                    if (-not (Test-Path -LiteralPath $source)) {
                        return;
                    }

                    New-Item -ItemType Directory -Force -Path $destination | Out-Null;
                    Get-ChildItem -LiteralPath $source -Force -ErrorAction SilentlyContinue |
                        ForEach-Object {
                            Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force -ErrorAction Stop;
                        };
                }

                function Backup-LudrynData {
                    New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null;
                    Copy-LudrynDirectory $externalData (Join-Path $backupRoot 'ExternalData');
                    Copy-LudrynDirectory $packageLocalState (Join-Path $backupRoot 'PackageLocalState');
                }

                function Restore-LudrynData {
                    Copy-LudrynDirectory (Join-Path $backupRoot 'ExternalData') $externalData;
                    Copy-LudrynDirectory (Join-Path $backupRoot 'PackageLocalState') $packageLocalState;
                }

                Backup-LudrynData;
                Get-Process -Name 'Ludryn' -ErrorAction SilentlyContinue |
                    Stop-Process -Force -ErrorAction SilentlyContinue;

                try {
                    Add-AppxPackage -Path {{Quote(packagePath)}} -ForceApplicationShutdown -ForceUpdateFromAnyVersion -ErrorAction Stop;
                }
                catch {
                    $installError = $_;
                    $existing = Get-AppxPackage -Name 'Kauac.Ludryn';
                    if (-not $existing) {
                        throw $installError;
                    }

                    try {
                        Remove-AppxPackage -Package $existing.PackageFullName -PreserveApplicationData -ErrorAction Stop;
                    }
                    catch {
                        $remaining = Get-AppxPackage -Name 'Kauac.Ludryn';
                        if ($remaining) {
                            Remove-AppxPackage -Package $remaining.PackageFullName -ErrorAction Stop;
                        }
                    }

                    Restore-LudrynData;
                    Add-AppxPackage -Path {{Quote(packagePath)}} -ForceApplicationShutdown -ForceUpdateFromAnyVersion -ErrorAction Stop;
                }

                Restore-LudrynData;
                if (-not (Get-AppxPackage -Name 'Kauac.Ludryn')) {
                    throw 'O Windows nao confirmou a instalacao do Ludryn.'
                }
                """;
            await RunPowerShellAsync(installCommand);

            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = 100;
            _title.Text = "Ludryn instalado";
            _status.Text = "Agora selecione o Ludryn em Configuracoes > Jogos > Experiencia de tela inteira.";

            var openSettings = MessageBox.Show(
                this,
                "O Ludryn foi instalado como Xbox Home App.\n\nDeseja abrir as Configuracoes do Windows agora?",
                "Instalacao concluida",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (openSettings == DialogResult.Yes)
            {
                Process.Start(new ProcessStartInfo("ms-settings:gaming")
                {
                    UseShellExecute = true
                });
            }

            Close();
        }
        catch (Exception ex)
        {
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = 0;
            _title.Text = "Nao foi possivel instalar";
            _status.Text = ex.Message;
            MessageBox.Show(
                this,
                ex.ToString(),
                "Falha na instalacao do Ludryn",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            try
            {
                Directory.Delete(temporaryDirectory, true);
            }
            catch
            {
                // The temporary directory is cleaned by Windows if a file is still in use.
            }
        }
    }

    private void SetStatus(string status)
    {
        _status.Text = status;
        _status.Refresh();
    }

    private static string ExtractResource(string resourceName, string destination)
    {
        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Componente ausente: {resourceName}");
        using var target = File.Create(destination);
        source.CopyTo(target);
        return destination;
    }

    private static void TrustCertificate(string certificatePath)
    {
        using var certificate = new X509Certificate2(certificatePath);
        AddCertificateToStore(certificate, StoreName.TrustedPeople);
        AddCertificateToStore(certificate, StoreName.Root);
    }

    private static void AddCertificateToStore(X509Certificate2 certificate, StoreName storeName)
    {
        using var store = new X509Store(storeName, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        var existing = store.Certificates.Find(
            X509FindType.FindByThumbprint,
            certificate.Thumbprint,
            validOnly: false);

        if (existing.Count == 0)
        {
            store.Add(certificate);
        }
    }

    private static async Task RunPowerShellAsync(string command)
    {
        var encodedCommand = Convert.ToBase64String(
            System.Text.Encoding.Unicode.GetBytes(
                "$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';" + command));

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -OutputFormat Text -EncodedCommand {encodedCommand}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(GetReadablePowerShellError(error, output));
        }
    }

    private static string GetReadablePowerShellError(string error, string output)
    {
        var message = string.IsNullOrWhiteSpace(error) ? output : error;
        if (!message.Contains("#< CLIXML", StringComparison.OrdinalIgnoreCase))
        {
            return message.Trim();
        }

        try
        {
            var xmlStart = message.IndexOf("<Objs", StringComparison.OrdinalIgnoreCase);
            if (xmlStart < 0)
            {
                return "O Windows recusou a instalacao. Consulte os logs do instalador.";
            }

            var document = System.Xml.Linq.XDocument.Parse(message[xmlStart..]);
            var lines = document
                .Descendants()
                .Where(element => element.Name.LocalName is "S" or "ToString")
                .Select(element => System.Net.WebUtility.HtmlDecode(element.Value))
                .Select(value => value
                    .Replace("_x000D_", string.Empty, StringComparison.OrdinalIgnoreCase)
                    .Replace("_x000A_", Environment.NewLine, StringComparison.OrdinalIgnoreCase))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct()
                .ToList();

            return lines.Count > 0
                ? string.Join(Environment.NewLine, lines).Trim()
                : "O Windows recusou a instalacao. Consulte os logs do instalador.";
        }
        catch
        {
            return "O Windows recusou a instalacao. Consulte os logs do instalador.";
        }
    }

    private static string Quote(string path) =>
        $"'{path.Replace("'", "''")}'";
}
