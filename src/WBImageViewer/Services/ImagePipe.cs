using System.IO;
using System.IO.Pipes;
using System.Text;

namespace WBImageViewer.Services;

/// <summary>
/// Forwards "open image" requests from a newly launched process to the already-running
/// instance, which is what makes single-window mode behave like one application.
/// </summary>
internal static class ImagePipe
{
    private const string PipeName = "怪叔叔看图-单实例-v1";

    public static bool TrySend(IReadOnlyList<string> files)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            client.Connect(300);
            client.ReadTimeout = 800;
            client.WriteTimeout = 800;
            using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, leaveOpen: true);
            writer.WriteLine(string.Join('\n', files));
            return string.Equals(reader.ReadLine(), "OK", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    public static void Start(Action<IReadOnlyList<string>> onFiles)
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync().ConfigureAwait(false);

                    using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true);
                    using var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };

                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    var files = (line ?? string.Empty)
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Where(ImageFile.IsSupported).ToArray();
                    System.Windows.Application.Current.Dispatcher.Invoke(() => onFiles(files));
                    writer.WriteLine("OK");
                }
                catch
                {
                    await Task.Delay(200).ConfigureAwait(false);
                }
            }
        });
    }
}
