using System.IO;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Basilisco.Services;

/// <summary>
/// El reproductor de vídeo de WPF solo abre bien H.264 (y los formatos clásicos de Windows Media). Los vídeos en
/// VP9, AV1 o HEVC (muy habituales en descargas de YouTube, normalmente .webm o .mkv) se convierten una vez a H.264
/// con el conversor de Windows, que sí usa las extensiones de vídeo de la Microsoft Store. Sin sonido: es un fondo.
/// </summary>
public static class VideoConverter
{
    static readonly string[] Supported = ["avc1", "avc3", "mp4v"];
    static readonly string[] Unsupported = ["vp09", "vp08", "av01", "hvc1", "hev1"];

    /// <summary>True si el reproductor de Basilisco puede abrir el vídeo tal cual.</summary>
    public static bool IsPlayable(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".wmv")
            return true;
        if (extension is ".mkv" or ".webm" or ".avi")
            return false;

        // MP4 / MOV / M4V: se mira qué códec de vídeo lleva dentro (su código de cuatro letras).
        try
        {
            using var file = File.OpenRead(path);
            var buffer = new byte[Math.Min(file.Length, 4 << 20)];
            var read = file.Read(buffer, 0, buffer.Length);
            var head = System.Text.Encoding.ASCII.GetString(buffer, 0, read);
            if (Unsupported.Any(head.Contains))
                return false;
            if (Supported.Any(head.Contains))
                return true;

            // El índice puede estar al final del archivo.
            if (file.Length > buffer.Length)
            {
                file.Seek(-Math.Min(file.Length, 4 << 20), SeekOrigin.End);
                read = file.Read(buffer, 0, buffer.Length);
                var tail = System.Text.Encoding.ASCII.GetString(buffer, 0, read);
                if (Unsupported.Any(tail.Contains))
                    return false;
            }
            return true;
        }
        catch (IOException)
        {
            return true;
        }
    }

    /// <summary>Convierte el vídeo a H.264 (MP4, sin sonido) junto al original. Devuelve la ruta nueva, o null si Windows no puede.</summary>
    public static async Task<string?> ConvertAsync(string path)
    {
        try
        {
            var output = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "-h264.mp4");
            var input = await StorageFile.GetFileFromPathAsync(path);
            var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(output)!);
            var target = await folder.CreateFileAsync(Path.GetFileName(output), CreationCollisionOption.ReplaceExisting);

            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
            profile.Audio = null;
            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
            var prepared = await transcoder.PrepareFileTranscodeAsync(input, target, profile);
            if (!prepared.CanTranscode)
            {
                await target.DeleteAsync();
                return null;
            }
            await prepared.TranscodeAsync();
            return output;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
