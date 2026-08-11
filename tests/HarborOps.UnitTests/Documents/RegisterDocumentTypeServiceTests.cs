using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Documents;

public sealed class RegisterDocumentTypeServiceTests
{
    [Fact]
    public async Task Register_rejects_invalid_input()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterDocumentTypeService(dbContext);
        var request = new RegisterDocumentTypeRequest
        {
            Name = " ",
            MandatoryValidityPeriodDays = null,
            IssuingAuthority = string.Empty,
            RequiresSurvey = null,
        };

        var exception = await Assert.ThrowsAsync<RegisterDocumentTypeValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Equal(4, exception.Errors.Count);
        Assert.Contains(RegisterDocumentTypeValidator.NameField, exception.Errors);
        Assert.Contains(RegisterDocumentTypeValidator.MandatoryValidityPeriodDaysField, exception.Errors);
        Assert.Contains(RegisterDocumentTypeValidator.IssuingAuthorityField, exception.Errors);
        Assert.Contains(RegisterDocumentTypeValidator.RequiresSurveyField, exception.Errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task Register_rejects_invalid_name(string? name)
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterDocumentTypeService(dbContext);
        var request = CreateValidRequest() with { Name = name };

        var exception = await Assert.ThrowsAsync<RegisterDocumentTypeValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterDocumentTypeValidator.NameField, exception.Errors);
    }

    [Fact]
    public async Task Register_accepts_name_at_max_length()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterDocumentTypeService(dbContext);
        var name = new string('A', DocumentType.NameMaxLength);
        var request = CreateValidRequest() with { Name = name };

        var response = await service.RegisterAsync(request, CancellationToken.None);

        Assert.True(response.Id > 0);
    }

    [Fact]
    public async Task Register_rejects_name_exceeding_max_length()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterDocumentTypeService(dbContext);
        var name = new string('A', DocumentType.NameMaxLength + 1);
        var request = CreateValidRequest() with { Name = name };

        var exception = await Assert.ThrowsAsync<RegisterDocumentTypeValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterDocumentTypeValidator.NameField, exception.Errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task Register_rejects_invalid_issuing_authority(string? issuingAuthority)
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterDocumentTypeService(dbContext);
        var request = CreateValidRequest() with { IssuingAuthority = issuingAuthority };

        var exception = await Assert.ThrowsAsync<RegisterDocumentTypeValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterDocumentTypeValidator.IssuingAuthorityField, exception.Errors);
    }

    [Fact]
    public async Task Register_accepts_issuing_authority_at_max_length()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterDocumentTypeService(dbContext);
        var issuingAuthority = new string('B', DocumentType.IssuingAuthorityMaxLength);
        var request = CreateValidRequest() with { IssuingAuthority = issuingAuthority };

        var response = await service.RegisterAsync(request, CancellationToken.None);

        Assert.True(response.Id > 0);
    }

    [Fact]
    public async Task Register_rejects_issuing_authority_exceeding_max_length()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterDocumentTypeService(dbContext);
        var issuingAuthority = new string('B', DocumentType.IssuingAuthorityMaxLength + 1);
        var request = CreateValidRequest() with { IssuingAuthority = issuingAuthority };

        var exception = await Assert.ThrowsAsync<RegisterDocumentTypeValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterDocumentTypeValidator.IssuingAuthorityField, exception.Errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(3651)]
    public async Task Register_rejects_invalid_mandatory_validity_period_days(int? days)
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterDocumentTypeService(dbContext);
        var request = CreateValidRequest() with { MandatoryValidityPeriodDays = days };

        var exception = await Assert.ThrowsAsync<RegisterDocumentTypeValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterDocumentTypeValidator.MandatoryValidityPeriodDaysField, exception.Errors);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3650)]
    public async Task Register_accepts_boundary_mandatory_validity_period_days(int days)
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterDocumentTypeService(dbContext);
        var request = CreateValidRequest() with { MandatoryValidityPeriodDays = days };

        var response = await service.RegisterAsync(request, CancellationToken.None);

        var documentType = await dbContext
            .Set<DocumentType>()
            .SingleAsync(candidate => candidate.Id == response.Id, CancellationToken.None);
        Assert.Equal(days, documentType.MandatoryValidityPeriod);
    }

    [Fact]
    public async Task Register_rejects_missing_requires_survey()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterDocumentTypeService(dbContext);
        var request = CreateValidRequest() with { RequiresSurvey = null };

        var exception = await Assert.ThrowsAsync<RegisterDocumentTypeValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterDocumentTypeValidator.RequiresSurveyField, exception.Errors);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Register_accepts_explicit_requires_survey(bool requiresSurvey)
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterDocumentTypeService(dbContext);
        var request = CreateValidRequest() with { RequiresSurvey = requiresSurvey };

        var response = await service.RegisterAsync(request, CancellationToken.None);

        var documentType = await dbContext
            .Set<DocumentType>()
            .SingleAsync(candidate => candidate.Id == response.Id, CancellationToken.None);
        Assert.Equal(requiresSurvey, documentType.RequiresSurvey);
    }

    [Fact]
    public async Task Register_persists_document_type_and_returns_id()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterDocumentTypeService(dbContext);
        var request = CreateValidRequest();

        var response = await service.RegisterAsync(request, CancellationToken.None);

        var documentType = await dbContext
            .Set<DocumentType>()
            .SingleAsync(CancellationToken.None);

        Assert.True(response.Id > 0);
        Assert.Equal(response.Id, documentType.Id);
        Assert.Equal("Port State Certificate", documentType.Name);
        Assert.Equal(60, documentType.MandatoryValidityPeriod);
        Assert.Equal("Flag State Authority", documentType.IssuingAuthority);
        Assert.True(documentType.RequiresSurvey);
    }

    [Fact]
    public async Task Register_rejects_duplicate_name_ignoring_case_and_whitespace()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterDocumentTypeService(dbContext);
        var firstRequest = CreateValidRequest() with { Name = "Port State Certificate" };
        var duplicateRequest = CreateValidRequest() with { Name = " port state certificate " };

        await service.RegisterAsync(firstRequest, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<DuplicateDocumentTypeNameException>(
            () => service.RegisterAsync(duplicateRequest, CancellationToken.None));

        Assert.Equal("port state certificate", exception.Name);
    }

    [Fact]
    public async Task Register_allows_different_names()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterDocumentTypeService(dbContext);
        var firstRequest = CreateValidRequest() with { Name = "Port State Certificate" };
        var secondRequest = CreateValidRequest() with { Name = "Safety Management Certificate" };

        await service.RegisterAsync(firstRequest, CancellationToken.None);
        var response = await service.RegisterAsync(secondRequest, CancellationToken.None);

        Assert.True(response.Id > 0);
        var documentTypeCount = await dbContext.Set<DocumentType>().CountAsync(CancellationToken.None);
        Assert.Equal(2, documentTypeCount);
    }

    private static RegisterDocumentTypeRequest CreateValidRequest()
    {
        return new RegisterDocumentTypeRequest
        {
            Name = "Port State Certificate",
            MandatoryValidityPeriodDays = 60,
            IssuingAuthority = "Flag State Authority",
            RequiresSurvey = true,
        };
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
