using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace ClientApp.ViewModels
{
    public class ImageItem : INotifyPropertyChanged
    {
        public string FilePath { get; set; }
        public string FileName => Path.GetFileName(FilePath);
        public BitmapSource Original { get; set; }
        private BitmapSource _processed;
        public BitmapSource Processed
        {
            get => _processed;
            set { _processed = value; OnPropertyChanged(); }
        }
        private string _status = "Ожидает";
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
