using PD.Mortgages.Application.Services;

namespace PD.Mortgages.Tests;

public sealed class DocumentUploadValidatorTests
{
    [Fact]
    public async Task ValidatesFileSignatureAndRemovesClientSuppliedPath()
    {
        var validator = new DocumentUploadValidator();
        await using var contents = new MemoryStream("%PDF-test"u8.ToArray());

        var result = await validator.ValidateAsync(@"C:\fakepath\identity.pdf", contents.Length, contents, existingDocumentCount: 0, existingBytes: 0, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Equal("identity.pdf", result.SafeFileName);
        Assert.Equal("%PDF-test", System.Text.Encoding.ASCII.GetString(result.Contents!));
    }

    [Fact]
    public async Task RejectsExtensionAndContentMismatch()
    {
        var validator = new DocumentUploadValidator();
        await using var contents = new MemoryStream("not a pdf"u8.ToArray());

        var result = await validator.ValidateAsync("identity.pdf", contents.Length, contents, existingDocumentCount: 0, existingBytes: 0, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains("does not match", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsUploadsThatExceedApplicationDocumentLimitsBeforeReading()
    {
        var validator = new DocumentUploadValidator();

        var result = await validator.ValidateAsync("identity.pdf", 1, Stream.Null, existingDocumentCount: DocumentUploadLimits.MaximumDocumentCount, existingBytes: 0, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains("reached its document limit", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
