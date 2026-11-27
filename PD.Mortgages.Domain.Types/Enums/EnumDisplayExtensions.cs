using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace PD.Mortgages.Domain.Types.Enums;

public static partial class EnumDisplayExtensions
{
    public static string GetDisplayName<TEnum>(this TEnum value)
        where TEnum : struct, Enum
    {
        var name = Enum.GetName(value);
        if (name is null)
        {
            return value.ToString();
        }

        var displayName = typeof(TEnum)
            .GetField(name)
            ?.GetCustomAttributes(typeof(DisplayAttribute), false)
            .OfType<DisplayAttribute>()
            .SingleOrDefault()
            ?.GetName();

        return displayName ?? PascalCaseBoundaryRegex().Replace(name, " ");
    }

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex PascalCaseBoundaryRegex();
}
