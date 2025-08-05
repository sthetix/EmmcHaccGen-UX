using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using EmmcHaccGen.GUI.Utils;
using MessageBox.Avalonia.DTO;
using MessageBox.Avalonia.Enums;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace EmmcHaccGen.GUI
{
    public partial class MainWindow : Window
    {
        private LibEmmcHaccGen? _lib = null;

        public MainWindow()
        {
            InitializeComponent();
            ProdKeysInputButton.Command = new LambdaCommand(x => OnBrowseProdKeys());
            FirmwareInputButton.Command = new LambdaCommand(x => OnBrowseFirmware());
            ProdKeysInput.TextChanged += (x, y) => Dispatcher.UIThread.Post(OnFileInputChanged);
            FirmwareInput.TextChanged += (x, y) => Dispatcher.UIThread.Post(OnFileInputChanged);
            GenerateButton.Command = new LambdaCommand(x => Generate());

            if (File.Exists("prod.keys"))
                ProdKeysInput.Text = "prod.keys";
        }

        private async Task OnBrowseProdKeys()
        {
            OpenFileDialog dialog = new()
            {
                Filters = new()
                {
                    new()
                    {
                        Name = "Keys",
                        Extensions = {"keys"}
                    }
                }
            };
            dialog.AllowMultiple = false;
            string[]? results = await dialog.ShowAsync(this);

            if (results == null || results.Length < 1)
                return;

            string result = results[0];

            if (!string.IsNullOrWhiteSpace(result))
            {
                ProdKeysInput.Text = result;
            }
        }

        private async Task OnBrowseFirmware()
        {
            OpenFolderDialog dialog = new();
            string? result = await dialog.ShowAsync(this);
            if (!string.IsNullOrWhiteSpace(result))
            {
                FirmwareInput.Text = result;
            }
        }

        private async void OnFileInputChanged()
        {
            _lib = null;
            StageTwoPanel.IsEnabled = false;

            if (!File.Exists(ProdKeysInput.Text) || !Directory.Exists(FirmwareInput.Text))
            {
                FileStatus.Content = "Cannot find required files!";
                return;
            }

            try
            {
                _lib = new LibEmmcHaccGen(ProdKeysInput.Text, FirmwareInput.Text);
            }
            catch (Exception e)
            {
                FileStatus.Content = e.Message;
                return;
            }

            FileStatus.Content = $"Firmware {_lib.NcaIndexer.Version}";
            StageTwoPanel.IsEnabled = true;

            // ExFAT toggle logic is removed as it is now always enabled.
        }

        private async void Generate()
        {
            OpenFolderDialog dialog = new();
            string? result = await dialog.ShowAsync(this);
            if (string.IsNullOrWhiteSpace(result) || !Directory.Exists(result) || _lib == null)
            {
                return;
            }

            // Get console type from the ComboBox
            bool mariko = ConsoleTypeComboBox.SelectedIndex == 1;

            // AutoRCM and ExFAT are hardcoded as requested.
            bool autorcm = false;
            bool exfat = true;

            string prefix = (mariko) ? "a" : "NX";
            string destFolder = $"{prefix}-{_lib.NcaIndexer.Version}" + (exfat ? "_exFAT" : "");
            string destPath = Path.Join(result, destFolder);

            if (Directory.Exists(destPath))
            {
                var msBox = MessageBoxManager
                    .GetMessageBoxStandard(new MessageBoxStandardParams
                    {
                        ButtonDefinitions = ButtonEnum.YesNo,
                        ContentTitle = "Output already exists",
                        ContentMessage = "A firmware folder already exists at the chosen destination. Overwrite?"
                    });

                var msBoxResult = await msBox.ShowAsync();

                if (msBoxResult != ButtonResult.Yes)
                    return;

                Directory.Delete(destPath, true);
            }

            Directory.CreateDirectory(destPath);

            try
            {
                _lib.WriteBis(destPath, autorcm, exfat, mariko);
                _lib.WriteSystem(destPath, exfat, _lib.NcaIndexer.RequiresV5Save || mariko, false);
            }
            catch (Exception e)
            {
                await MessageBoxManager.GetMessageBoxStandard(new()
                {
                    ButtonDefinitions = ButtonEnum.Ok,
                    ContentTitle = "Failure Generating Firmware",
                    ContentMessage = e.Message
                }).ShowAsync();
                return;
            }

            OpenFolder(destPath);
        }

        private static void OpenFolder(string path)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start("explorer.exe", "\"" + path.Replace("/", "\\") + "\"");
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start("xdg-open", $"\"{path}\"");
        }
    }
}