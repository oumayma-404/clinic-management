using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClinicManagement.API.Swagger;

public class FileUploadOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var formParameters = context.ApiDescription.ParameterDescriptions
            .Where(p => p.Source == BindingSource.Form)
            .ToList();

        if (!formParameters.Any()) return;

        var fileParameters = formParameters
            .Where(p => p.ModelMetadata?.ModelType == typeof(IFormFile) || 
                       (p.ModelMetadata?.ModelType != null && 
                        typeof(IFormFile).IsAssignableFrom(p.ModelMetadata.ModelType)))
            .ToList();

        // Also check for complex objects that might contain IFormFile properties
        var hasFileParameter = fileParameters.Any() || 
            formParameters.Any(p => p.ModelMetadata?.Properties != null && 
                p.ModelMetadata.Properties.Any(prop => 
                    prop.ModelType == typeof(IFormFile) || 
                    typeof(IFormFile).IsAssignableFrom(prop.ModelType)));

        if (hasFileParameter || formParameters.Any(p => p.Source == BindingSource.Form))
        {
            // Clear existing parameters that are form-based to prevent Swashbuckle errors
            operation.Parameters = operation.Parameters?
                .Where(p => !formParameters.Any(fp => fp.Name == p.Name))
                .ToList() ?? new List<OpenApiParameter>();

            var properties = new Dictionary<string, OpenApiSchema>();
            var required = new HashSet<string>();

            foreach (var param in formParameters)
            {
                // Handle IFormFile directly
                if (param.ModelMetadata?.ModelType == typeof(IFormFile) || 
                    (param.ModelMetadata?.ModelType != null && 
                     typeof(IFormFile).IsAssignableFrom(param.ModelMetadata.ModelType)))
                {
                    properties[param.Name] = new OpenApiSchema
                    {
                        Type = "string",
                        Format = "binary"
                    };
                    if (param.IsRequired)
                    {
                        required.Add(param.Name);
                    }
                }
                // Handle complex objects - extract their properties
                else if (param.ModelMetadata?.ModelType != null && 
                         param.ModelMetadata.ModelType != typeof(string) &&
                         !param.ModelMetadata.ModelType.IsPrimitive &&
                         param.ModelMetadata.ModelType != typeof(DateTime) &&
                         param.ModelMetadata.ModelType != typeof(Guid) &&
                         param.ModelMetadata.Properties != null)
                {
                    foreach (var prop in param.ModelMetadata.Properties)
                    {
                        var propName = prop.PropertyName ?? prop.Name;
                        var schema = prop.ModelType == typeof(IFormFile) || 
                                   typeof(IFormFile).IsAssignableFrom(prop.ModelType)
                            ? new OpenApiSchema { Type = "string", Format = "binary" }
                            : new OpenApiSchema { Type = GetSchemaType(prop.ModelType) };

                        properties[propName] = schema;

                        if (prop.IsRequired)
                        {
                            required.Add(propName);
                        }
                    }
                }
                // Handle simple types
                else
                {
                    var schema = new OpenApiSchema
                    {
                        Type = GetSchemaType(param.ModelMetadata?.ModelType)
                    };

                    properties[param.Name] = schema;

                    if (param.IsRequired)
                    {
                        required.Add(param.Name);
                    }
                }
            }

            if (properties.Any())
            {
                operation.RequestBody = new OpenApiRequestBody
                {
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["multipart/form-data"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema
                            {
                                Type = "object",
                                Properties = properties,
                                Required = required.Any() ? required : null
                            }
                        }
                    }
                };
            }
        }
    }

    private string GetSchemaType(Type? type)
    {
        if (type == null) return "string";
        
        if (type == typeof(string)) return "string";
        if (type == typeof(int) || type == typeof(int?)) return "integer";
        if (type == typeof(long) || type == typeof(long?)) return "integer";
        if (type == typeof(decimal) || type == typeof(decimal?)) return "number";
        if (type == typeof(double) || type == typeof(double?)) return "number";
        if (type == typeof(float) || type == typeof(float?)) return "number";
        if (type == typeof(bool) || type == typeof(bool?)) return "boolean";
        if (type == typeof(DateTime) || type == typeof(DateTime?)) return "string";
        if (type == typeof(Guid) || type == typeof(Guid?)) return "string";
        
        return "string";
    }
}
