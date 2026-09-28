using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace Project.Models;

[AttributeUsage(AttributeTargets.Property)]
public sealed class NotFutureDateAttribute : ValidationAttribute, IClientModelValidator
{
    public override bool IsValid(object? value) =>
        value is null || value is DateOnly date && date <= DateOnly.FromDateTime(DateTime.Today);

    public void AddValidation(ClientModelValidationContext context)
    {
        context.Attributes.TryAdd("data-val", "true");
        context.Attributes.TryAdd("data-val-notfuturedate", FormatErrorMessage(context.ModelMetadata.GetDisplayName()));
        context.Attributes.TryAdd("data-val-notfuturedate-today",
            DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }
}