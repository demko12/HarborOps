using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;

namespace HarborOps.UnitTests.Documents;

public sealed class UploadDocumentFileValidatorTests
{
    [Fact]
    public void Validate_rejects_empty_file_name()
    {
        var request = CreateValidRequest() with { FileName = " " };

        var errors = UploadDocumentFileValidator.Validate(request, CreateOptions());

        Assert.Contains(UploadDocumentFileValidator.FileNameField, errors);
    }

    [Fact]
    public void Validate_rejects_file_name_exceeding_max_length()
    {
        var longName = new string('a', Document.FileNameMaxLength + 1) + ".pdf";
        var request = CreateValidRequest() with { FileName = longName };

        var errors = UploadDocumentFileValidator.Validate(request, CreateOptions());

        Assert.Contains(UploadDocumentFileValidator.FileNameField, errors);
    }

    [Theory]
    [InlineData("../evil.pdf")]
    [InlineData("folder/evil.pdf")]
    [InlineData("folder\\evil.pdf")]
    [InlineData("evil..pdf")]
    public void Validate_rejects_file_name_with_path_segments(string fileName)
    {
        var request = CreateValidRequest() with { FileName = fileName };

        var errors = UploadDocumentFileValidator.Validate(request, CreateOptions());

        Assert.Contains(UploadDocumentFileValidator.FileNameField, errors);
    }

    [Fact]
    public void Validate_rejects_extension_not_in_allowed_list()
    {
        var request = CreateValidRequest() with { FileName = "malware.exe" };

        var errors = UploadDocumentFileValidator.Validate(request, CreateOptions());

        Assert.Contains(UploadDocumentFileValidator.FileNameField, errors);
    }

    [Theory]
    [InlineData("certificate.pdf")]
    [InlineData("certificate.PDF")]
    [InlineData("certificate.Pdf")]
    public void Validate_accepts_allowed_extension_case_insensitively(string fileName)
    {
        var request = CreateValidRequest() with { FileName = fileName };

        var errors = UploadDocumentFileValidator.Validate(request, CreateOptions());

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_rejects_empty_content_type()
    {
        var request = CreateValidRequest() with { ContentType = " " };

        var errors = UploadDocumentFileValidator.Validate(request, CreateOptions());

        Assert.Contains(UploadDocumentFileValidator.ContentTypeField, errors);
    }

    [Fact]
    public void Validate_rejects_content_type_exceeding_max_length()
    {
        var longContentType = new string('a', Document.ContentTypeMaxLength + 1);
        var request = CreateValidRequest() with { ContentType = longContentType };

        var errors = UploadDocumentFileValidator.Validate(request, CreateOptions());

        Assert.Contains(UploadDocumentFileValidator.ContentTypeField, errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_rejects_size_bytes_not_greater_than_zero(long sizeBytes)
    {
        var request = CreateValidRequest() with { SizeBytes = sizeBytes };

        var errors = UploadDocumentFileValidator.Validate(request, CreateOptions());

        Assert.Contains(UploadDocumentFileValidator.SizeBytesField, errors);
    }

    [Fact]
    public void Validate_throws_file_too_large_exception_when_size_exceeds_maximum()
    {
        var options = new DocumentFileValidationOptions { MaxSizeBytes = 1000 };
        var request = CreateValidRequest() with { SizeBytes = 1001 };

        var exception = Assert.Throws<FileTooLargeException>(
            () => UploadDocumentFileValidator.Validate(request, options));

        Assert.Equal(1001, exception.ActualSizeBytes);
        Assert.Equal(1000, exception.MaxSizeBytes);
    }

    [Fact]
    public void Validate_returns_no_errors_for_valid_request()
    {
        var request = CreateValidRequest();

        var errors = UploadDocumentFileValidator.Validate(request, CreateOptions());

        Assert.Empty(errors);
    }

    private static UploadDocumentFileRequest CreateValidRequest()
    {
        return new UploadDocumentFileRequest(
            Guid.NewGuid(),
            "certificate.pdf",
            "application/pdf",
            1024,
            new MemoryStream(),
            Guid.NewGuid());
    }

    private static DocumentFileValidationOptions CreateOptions()
    {
        return new DocumentFileValidationOptions();
    }
}
