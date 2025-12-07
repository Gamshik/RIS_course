using ClientApp.Services;
using Common.Messages;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace ClientApp.ViewModels
{
    public class ClientViewModel : INotifyPropertyChanged
    {
        private readonly ClientService _clientService;

        public ObservableCollection<ImageItem> Images { get; } = new();

        private string _progressText = "Выберите изображения и нажмите 'Отправить все'";
        public string ProgressText
        {
            get => _progressText;
            set { _progressText = value; OnPropertyChanged(); }
        }

        private bool _isProcessing;
        public bool IsProcessing
        {
            get => _isProcessing;
            set { _isProcessing = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
        }

        public ICommand SelectImagesCommand { get; }
        public ICommand SendAllCommand { get; }
        public ICommand ClearCommand { get; }

        public ClientViewModel()
        {
            _clientService = new ClientService("127.0.0.1", 5001);
            _clientService.ProgressUpdated += OnTaskUpateStatus;
            _clientService.TaskCompleted += OnTaskCompleted;
            _clientService.ErrorOccurred += e => Application.Current.Dispatcher.Invoke(() => ProgressText = "ОШИБКА: " + e);

            SelectImagesCommand = new RelayCommand(SelectImages);

            SendAllCommand = new RelayCommand(
                async () => await SendAll(),
                () => CanSendAll()       
            );

            ClearCommand = new RelayCommand(() => Images.Clear());
        }

        private void SelectImages()
        {
            var dlg = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp"
            };
            if (dlg.ShowDialog() == true)
            {
                foreach (var path in dlg.FileNames)
                {
                    if (Images.Any(i => i.FilePath == path)) continue;

                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(path);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze();

                        Images.Add(new ImageItem
                        {
                            FilePath = path,
                            Original = bitmap,
                            Status = "Готов к отправке"
                        });
                    }
                    catch { /* плохой файл — пропускаем */ }
                }
            }
        }

        private bool CanSendAll() => !IsProcessing && Images.Any(i => i.Status == "Готов к отправке" || i.Status.Contains("Ошибка"));

        private async Task SendAll()
        {
            if (!Images.Any()) return;

            IsProcessing = true;
            ProgressText = $"Отправка {Images.Count} изображений...";

            var batchId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var batch = new BatchRequestMessage(
                batchId,
                Images.Select((item, index) => new ImageMessage(
                    (int)(batchId + index),
                    item.FileName,
                    (int)item.Original.Width,
                    (int)item.Original.Height,
                    1,
                    File.ReadAllBytes(item.FilePath)
                )).ToList()
            );

            try
            {
                await _clientService.SendBatchAndReceiveResultAsync(batch, CancellationToken.None);
            }
            catch (Exception ex)
            {
                ProgressText = $"Ошибка: {ex.Message}";
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private void OnTaskUpateStatus(UpdateTaskStatusData data)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var item = Images.FirstOrDefault(i => i.FileName == data.FileName);

                Debug.WriteLine($"Data FILENAME: {data.FileName}");

                if (item != null)
                {
                    item.Status = data.StatusText;
                }
            });
        }

        private void OnTaskCompleted(ImageMessage result)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var item = Images.FirstOrDefault(i => result.FileName.EndsWith(i.FileName, StringComparison.OrdinalIgnoreCase));

                if (item != null)
                {
                    item.Processed = BytesToBitmapSource(result.ImageData);
                }

                if (Images.All(i => i.Status == "Готово" || i.Status.Contains("Ошибка")))
                {
                    ProgressText = "ВСЕ ЗАДАЧИ ЗАВЕРШЕНЫ!";
                    IsProcessing = false;
                }
            });
        }

        private BitmapSource? BytesToBitmapSource(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }

            try
            {
                using var ms = new MemoryStream(bytes);
                var image = new BitmapImage();

                image.BeginInit();
             
                image.CacheOption = BitmapCacheOption.OnLoad; 
                image.StreamSource = ms;

                image.EndInit();
                
                image.Freeze(); 
               
                return image;
            }
            catch (Exception ex) when (ex is EndOfStreamException || ex is IOException || ex is NotSupportedException || ex is ArgumentException)
            {
                Debug.WriteLine($"[BytesToBitmapSource] Ошибка при загрузке изображения: {ex.GetType().Name} - {ex.Message}");
                return null;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public class RelayCommand : ICommand
        {
            private readonly Action _execute;
            private readonly Func<bool> _canExecute;  

            public RelayCommand(Action execute, Func<bool> canExecute = null)
            {
                _execute = execute ?? throw new ArgumentNullException(nameof(execute));
                _canExecute = canExecute;
            }

            public bool CanExecute(object parameter) => _canExecute == null || _canExecute();

            public void Execute(object parameter) => _execute();

            public event EventHandler CanExecuteChanged
            {
                add { CommandManager.RequerySuggested += value; }
                remove { CommandManager.RequerySuggested -= value; }
            }
        }
    }
}