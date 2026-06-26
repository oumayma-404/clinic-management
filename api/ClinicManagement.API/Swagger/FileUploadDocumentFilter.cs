using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Linq;

namespace ClinicManagement.API.Swagger;

public class FileUploadDocumentFilter : IDocumentFilter
{
    public void Apply(Microsoft.OpenApi.Models.OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        // This filter runs early and can help configure the document
        // The actual work is done by the operation and parameter filters
    }
}








