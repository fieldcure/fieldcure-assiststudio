using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace TestMcpServer;

/// <summary>
/// MCP tools that return various content block types for testing AssistStudio's
/// multimedia rendering pipeline. Uses real sample files from the Samples directory.
/// </summary>
[McpServerToolType]
public static class TestMediaTools
{
    /// <summary>
    /// Resolves a sample file path relative to the executable directory.
    /// </summary>
    private static string SamplePath(string filename) =>
        Path.Combine(AppContext.BaseDirectory, "Samples", filename);

    /// <summary>
    /// Returns a JPEG image as an ImageContentBlock.
    /// Tests the basic image rendering path.
    /// </summary>
    [McpServerTool(Name = "test_image")]
    [Description("Returns a sample JPEG image as ImageContentBlock")]
    public static CallToolResult TestImage()
    {
        var bytes = File.ReadAllBytes(SamplePath("sample.jpg"));
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = $"Sample JPEG image ({bytes.Length / 1024}KB)." },
                ImageContentBlock.FromBytes(new ReadOnlyMemory<byte>(bytes), "image/jpeg")
            ]
        };
    }

    /// <summary>
    /// Returns an MP3 audio file as an AudioContentBlock.
    /// Tests the audio inline player rendering.
    /// </summary>
    [McpServerTool(Name = "test_audio")]
    [Description("Returns a sample MP3 audio as AudioContentBlock")]
    public static CallToolResult TestAudio()
    {
        var bytes = File.ReadAllBytes(SamplePath("sample.mp3"));
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = $"Sample MP3 audio ({bytes.Length / 1024}KB)." },
                AudioContentBlock.FromBytes(new ReadOnlyMemory<byte>(bytes), "audio/mpeg")
            ]
        };
    }

    /// <summary>
    /// Returns an MP4 video as an EmbeddedResourceBlock with blob.
    /// Tests the video inline player rendering.
    /// </summary>
    [McpServerTool(Name = "test_video")]
    [Description("Returns a sample MP4 video as EmbeddedResourceBlock (video/mp4, blob included)")]
    public static CallToolResult TestVideo()
    {
        var bytes = File.ReadAllBytes(SamplePath("sample.mp4"));
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = $"Sample MP4 video ({bytes.Length / 1024}KB)." },
                new EmbeddedResourceBlock
                {
                    Resource = BlobResourceContents.FromBytes(
                        new ReadOnlyMemory<byte>(bytes),
                        "file:///sample.mp4",
                        "video/mp4")
                }
            ]
        };
    }

    /// <summary>
    /// Returns an EmbeddedResourceBlock with an HTTP URI and no blob data.
    /// Tests the download link rendering path for non-renderable MIME types.
    /// </summary>
    [McpServerTool(Name = "test_download")]
    [Description("Returns a sample PDF as EmbeddedResourceBlock (application/pdf, blob included)")]
    public static CallToolResult TestDownload()
    {
        var bytes = File.ReadAllBytes(SamplePath("sample.pdf"));
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = $"Sample PDF document ({bytes.Length / 1024}KB)." },
                new EmbeddedResourceBlock
                {
                    Resource = BlobResourceContents.FromBytes(
                        new ReadOnlyMemory<byte>(bytes),
                        "file:///sample.pdf",
                        "application/pdf")
                }
            ]
        };
    }

    /// <summary>
    /// Returns a large MP3 AudioContentBlock exceeding the temp file threshold (10MB).
    /// Tests the data URI to temp file to file:// URI conversion path.
    /// </summary>
    [McpServerTool(Name = "test_large_media")]
    [Description("Returns a 14MB MP3 audio as AudioContentBlock for temp file testing")]
    public static CallToolResult TestLargeMedia()
    {
        var bytes = File.ReadAllBytes(SamplePath("large_sample.mp3"));
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = $"Large MP3 audio ({bytes.Length / 1024}KB)." },
                AudioContentBlock.FromBytes(new ReadOnlyMemory<byte>(bytes), "audio/mpeg")
            ]
        };
    }
}
