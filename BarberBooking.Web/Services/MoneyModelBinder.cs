using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BarberBooking.Web.Services;

/// <summary>Accepts either decimal separator, without interpreting a separator as a thousands group.</summary>
public sealed class MoneyModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext context)
    {
        var input = context.ValueProvider.GetValue(context.ModelName);
        if (input != ValueProviderResult.None) context.ModelState.SetModelValue(context.ModelName, input);
        var text = input.FirstValue?.Trim().Replace(',', '.');
        if (!string.IsNullOrEmpty(text) && decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            context.Result = ModelBindingResult.Success(value);
        else
        {
            context.ModelState.TryAddModelError(context.ModelName, "Укажите цену числом, например 800,50 или 800.50.");
            context.Result = ModelBindingResult.Failed();
        }
        return Task.CompletedTask;
    }
}
