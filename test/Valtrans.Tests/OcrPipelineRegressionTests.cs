using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class OcrPipelineRegressionTests
{
    [Fact]
    public void Sanitizer_filters_chat_labels_and_preserves_messages()
    {
        foreach (var label in new[] { "팀:", "팀: |", "팀: not yet sent", "오늘", "(방송) 나 (피닉스) 님이 장비를 요청합니다!" })
            Assert.Equal("", ChatTextSanitizer.NormalizeOcrBody(label));
        Assert.Equal("오늘", ChatTextSanitizer.NormalizeOcrBody("(팀) 나: 오늘"));
        Assert.Equal("no enemies left", ChatTextSanitizer.NormalizeOcrBody("(팀) 나: no enemies left"));
        Assert.True(ChatTextSanitizer.LooksLikeChatInputLine("팀: |"));
        Assert.False(ChatTextSanitizer.LooksLikeChatInputLine("(팀) Player: hello"));
    }
}
