using System;
using System.Reflection;
using System.Runtime.Serialization;

namespace HermaFx
{
	/// <summary>
	/// Utility methods intended for working with EnumMemberAttribute.
	/// </summary>
	/// <remarks>
	/// This is specially needed when using special chars on the EnumMember values.
	/// </remarks>
	public static class EnumMemberExtensions
	{
		public static string ToString<TEnum>(TEnum enumValue)
		{
			if (enumValue == null)
				return null;

			var type = Nullable.GetUnderlyingType(typeof(TEnum)) ?? typeof(TEnum);
			var name = Enum.GetName(type, enumValue);

			if (name == null)
				return null;

			var field = type.GetField(name);
			var attr = (EnumMemberAttribute)Attribute.GetCustomAttribute(field, typeof(EnumMemberAttribute));

			return attr != null ? attr.Value : name;
		}

		public static TEnum? TryParse<TEnum>(string value) where TEnum : struct, Enum
		{
			var type = typeof(TEnum);

			foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
			{
				var attr = (EnumMemberAttribute)Attribute.GetCustomAttribute(field, typeof(EnumMemberAttribute));
				var enumValue = attr?.Value ?? field.Name;

				if (string.Equals(enumValue, value, StringComparison.OrdinalIgnoreCase))
				{
					return (TEnum)field.GetValue(null);
				}
			}

			return null;
		}
	}
}
