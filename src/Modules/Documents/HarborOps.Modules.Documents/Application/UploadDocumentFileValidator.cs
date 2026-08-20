using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;

namespace HarborOps.Modules.Documents.Application;

internal static class UploadDocumentFileValidator
{
    internal const string FileNameField = "fileName";
    internal const string ContentTypeField = "contentType";
    internal const string SizeBytesField = "sizeBytes";

    internal static IReadOnlyDictionary<string, string[]> Validate(
        UploadDocumentFileRequest request,
        DocumentFileValidationOptions options)
    {
        if (request.SizeBytes > options.MaxSizeBytes)
        {
            throw new FileTooLargeException(request.SizeBytes, options.MaxSizeBytes);
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateFileName(request.FileName, options, errors);
        ValidateContentType(request.ContentType, errors);
        ValidateSizeBytes(request.SizeBytes, errors);

        return errors;
    }

    private static void ValidateFileName(
        string fileName,
        DocumentFileValidationOptions options,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            errors[FileNameField] = ["File name is required."];
            return;
        }

        if (fileName.Length > Document.FileNameMaxLength)
        {
            errors[FileNameField] = [$"File name must not exceed {Document.FileNameMaxLength} characters."];
            return;
        }

        if (fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains(".."))
        {
            errors[FileNameField] = ["File name must not contain path segments."];
            return;
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            errors[FileNameField] = ["File type is not allowed."];
        }
    }

    private static void ValidateContentType(string contentType, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            errors[ContentTypeField] = ["Content type is required."];
            return;
        }

        if (contentType.Length > Document.ContentTypeMaxLength)
        {
            errors[ContentTypeField] = [$"Content type must not exceed {Document.ContentTypeMaxLength} characters."];
        }
    }

    private static void ValidateSizeBytes(long sizeBytes, IDictionary<string, string[]> errors)
    {
        if (sizeBytes <= 0)
        {
            errors[SizeBytesField] = ["File size must be greater than zero."];
        }
    }
}
