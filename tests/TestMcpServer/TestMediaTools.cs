using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace TestMcpServer;

/// <summary>
/// MCP tools that return various content block types for testing AssistStudio's
/// multimedia rendering pipeline.
/// </summary>
[McpServerToolType]
public static class TestMediaTools
{
    /// <summary>
    /// Returns a solid-color PNG image as an ImageContentBlock.
    /// Tests the basic image rendering path.
    /// </summary>
    [McpServerTool(Name = "test_image")]
    [Description("Returns a 16x16 PNG image as ImageContentBlock")]
    public static CallToolResult TestImage()
    {
        var pngBytes = SampleDataGenerator.GeneratePng(width: 16, height: 16);
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = "Generated a 16x16 test image." },
                ImageContentBlock.FromBytes(new ReadOnlyMemory<byte>(pngBytes), "image/png")
            ]
        };
    }

    /// <summary>
    /// Returns a 1-second sine wave WAV as an AudioContentBlock.
    /// Tests the audio inline player rendering.
    /// </summary>
    [McpServerTool(Name = "test_audio")]
    [Description("Returns a 1-second 440Hz sine wave WAV as AudioContentBlock")]
    public static CallToolResult TestAudio()
    {
        var wavBytes = SampleDataGenerator.GenerateWav(durationSeconds: 1.0, frequency: 440.0);
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = "Generated a 1-second 440Hz sine wave." },
                AudioContentBlock.FromBytes(new ReadOnlyMemory<byte>(wavBytes), "audio/wav")
            ]
        };
    }

    /// <summary>
    /// Returns arbitrary bytes as an EmbeddedResourceBlock with video/mp4 MIME type.
    /// The data is not a valid MP4, so the video player should show a fallback message.
    /// Tests the video rendering + fallback path.
    /// </summary>
    [McpServerTool(Name = "test_video")]
    [Description("Returns fake MP4 bytes as EmbeddedResourceBlock (video/mp4, blob included)")]
    public static CallToolResult TestVideo()
    {
        var fakeVideoBytes = SampleDataGenerator.GenerateBytes(1024);
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = "Generated fake MP4 data (will trigger fallback)." },
                new EmbeddedResourceBlock
                {
                    Resource = BlobResourceContents.FromBytes(
                        new ReadOnlyMemory<byte>(fakeVideoBytes),
                        "file:///test-video.mp4",
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
    [Description("Returns EmbeddedResourceBlock with HTTP URI only (application/pdf, no blob)")]
    public static CallToolResult TestDownload()
    {
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = "Here's a download link to a sample PDF." },
                new EmbeddedResourceBlock
                {
                    Resource = new BlobResourceContents
                    {
                        Uri = "https://example.com/sample.pdf",
                        MimeType = "application/pdf",
                        Blob = ReadOnlyMemory<byte>.Empty
                    }
                }
            ]
        };
    }

    /// <summary>
    /// Returns a large WAV AudioContentBlock exceeding the temp file threshold (10MB).
    /// Tests the data URI to temp file to file:// URI conversion path.
    /// </summary>
    [McpServerTool(Name = "test_large_media")]
    [Description("Returns a large WAV AudioContentBlock (default 12MB) for temp file testing")]
    public static CallToolResult TestLargeMedia(
        [Description("Size in megabytes (default: 12)")] int sizeMb = 12)
    {
        var wavBytes = SampleDataGenerator.GenerateLargeWav(sizeMb);
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = $"Generated a {sizeMb}MB WAV file for large media testing." },
                AudioContentBlock.FromBytes(new ReadOnlyMemory<byte>(wavBytes), "audio/wav")
            ]
        };
    }
}
