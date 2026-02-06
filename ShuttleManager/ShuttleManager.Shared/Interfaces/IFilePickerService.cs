namespace ShuttleManager.Shared.Interfaces;

public interface IFilePickerService
{
    Task<PickedFileDto?> PickFileAsync();
}

public class PickedFileDto
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public Stream? DataStream { get; set; } 
    public string ContentType { get; set; } = string.Empty; 
}