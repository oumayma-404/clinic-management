using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Linq;

namespace ClinicManagement.API.Swagger;

public class FileUploadParameterFilter : IParameterFilter
{
    public void Apply(Microsoft.OpenApi.Models.OpenApiParameter parameter, ParameterFilterContext context)
    {
        var parameterDescription = context.ApiParameterDescription;
        
        // If this is a form parameter with IFormFile, configure it to prevent Swashbuckle errors
        if (parameterDescription?.Source == BindingSource.Form)
        {
            if (parameterDescription.ModelMetadata?.ModelType == typeof(IFormFile))
            {
                // Set schema for IFormFile to prevent the error
                parameter.Schema = new Microsoft.OpenApi.Models.OpenApiSchema
                {
                    Type = "string",
                    Format = "binary"
                };
                parameter.In = Microsoft.OpenApi.Models.ParameterLocation.Query; // Temporary, will be moved to request body by operation filter
            }
            else
            {
                // For other form parameters, ensure they have a schema
                if (parameter.Schema == null)
                {
                    parameter.Schema = new Microsoft.OpenApi.Models.OpenApiSchema
                    {
                        Type = "string"
                    };
                }
            }
        }
    }
}

