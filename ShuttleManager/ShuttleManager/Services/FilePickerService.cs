using ShuttleManager.Shared.Services.FilePicker;


namespace ShuttleManager.Services;

public class FilePickerService : IFilePickerService
{
    public async Task<PickedFileDto?> PickFileAsync()
    {

        var options = new PickOptions
        {
            PickerTitle = "Выберите .bin файл",
            FileTypes = new FilePickerFileType(
                new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.Android, new[] { ".bin" } },
                    { DevicePlatform.iOS, new[] { ".bin" } },
                    { DevicePlatform.WinUI, new[] { ".bin" } },
                    { DevicePlatform.Tizen, new[] { ".bin" } },
                    { DevicePlatform.macOS, new[] { ".bin" } }
                })
        };


        var result = await FilePicker.Default.PickAsync(options);
        if (result == null) return null;

        return new PickedFileDto
        {
            FileName = result.FileName,
            FilePath = result.FullPath,
            DataStream = await result.OpenReadAsync(),
            ContentType = result.ContentType
        };
    }
}
