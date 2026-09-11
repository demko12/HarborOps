using System.Text;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Cargo.Presentation;

[ApiController]
[Route("api/cargo/container-manifest-template")]
public sealed class ContainerManifestTemplateController : ControllerBase
{
    [HttpGet]
    [Authorize("cargo.read")]
    public IActionResult Download([FromQuery] string? format)
    {
        return format?.ToLowerInvariant() switch
        {
            "xml" => File(Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<ContainerManifest>\n  <Container><ContainerNumber>MSKU1234567</ContainerNumber><ContainerType>22G1</ContainerType><POL>NLRTM</POL><POD>USNYC</POD><WeightKg>12500.000</WeightKg></Container>\n</ContainerManifest>"), "application/xml", "container-manifest-template.xml"),
            "xlsx" => File(CreateWorkbook(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "container-manifest-template.xlsx"),
            _ => BadRequest(new ProblemDetails { Title = "Format must be xlsx or xml." }),
        };
    }

    private static byte[] CreateWorkbook()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Container manifest");
        sheet.Cell(1, 1).Value = "ContainerNumber";
        sheet.Cell(1, 2).Value = "ContainerType";
        sheet.Cell(1, 3).Value = "POL";
        sheet.Cell(1, 4).Value = "POD";
        sheet.Cell(1, 5).Value = "WeightKg";
        sheet.Cell(2, 1).Value = "MSKU1234567";
        sheet.Cell(2, 2).Value = "22G1";
        sheet.Cell(2, 3).Value = "NLRTM";
        sheet.Cell(2, 4).Value = "USNYC";
        sheet.Cell(2, 5).Value = 12500m;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
