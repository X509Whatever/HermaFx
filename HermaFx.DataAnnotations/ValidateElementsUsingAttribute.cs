#if true
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;

namespace HermaFx.DataAnnotations
{
	[AttributeUsage(AttributeTargets.Property)]
	public class ValidateElementsUsingAttribute : ValidationAttribute
	{
		#region Private Properties
		private const string DefaultErrorMessage = "{0} has some invalid values.";
		#endregion

		#region Public Properties
		public static MemberTypes DefaultMemberTypes { get; } = MemberTypes.Property;
		public static BindingFlags DefaultBindingFlags { get; } = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;

		public Type MetadataType { get; private set; }
		public string Property { get; private set; }
		public MemberTypes MemberTypes { get; }
		public BindingFlags BindingFlags { get; }

		/// <summary>
		/// A flag indicating that the attribute requires a non-null <see cref=System.ComponentModel.DataAnnotations.ValidationContext /> to perform validation.
		/// </summary>
		public override bool RequiresValidationContext
		{
			get
			{
				return true;
			}
		}
		#endregion

		#region .ctor

		public ValidateElementsUsingAttribute(Type metadataType, string propertyName)
			: this(metadataType, propertyName, DefaultErrorMessage) { }

		public ValidateElementsUsingAttribute(Type metadataType, string propertyName, string errorMessage)
			: this(metadataType, propertyName, DefaultBindingFlags, DefaultMemberTypes, errorMessage)
		{
		}
		public ValidateElementsUsingAttribute(Type metadataType, string propertyName, BindingFlags bindingFlags, MemberTypes memberTypes)
			: this(metadataType, propertyName, bindingFlags, memberTypes, DefaultErrorMessage)
		{
		}

		public ValidateElementsUsingAttribute(Type metadataType, string propertyName, BindingFlags bindingFlags, MemberTypes memberTypes, string errorMessage)
			: base(() => errorMessage)
		{
			MetadataType = metadataType;
			Property = propertyName;
			BindingFlags = bindingFlags;
			MemberTypes = memberTypes;
		}

		#endregion

		protected virtual void CheckTargetType(object value, string memberName)
		{
			if (!(value is IEnumerable))
				throw new ValidationException($"Property {memberName} is not enumerable.");
		}

		protected virtual IEnumerable GetEnumerable(object value) => value as IEnumerable;

		private IEnumerable<ValidationResult> ValidateClass(object value, ValidationContext context, IEnumerable<ValidationAttribute> attributes)
		{
			var results = new List<ValidationResult>();
			Validator.TryValidateValue(value, context, results, attributes);

			return results;
		}

		protected virtual MemberInfo GetMember(string name)
		{
			var member = MetadataType.GetMember(name, BindingFlags).SingleOrDefault(x => MemberTypes.HasFlag(x.MemberType));
			return member;
		}

		private static ValidationContext CreateContextFor(object item, ValidationContext context, int idx2)
		{
			return new ValidationContext(item, null, context.Items)
			{
				DisplayName = context.DisplayName.IfNotNull(x => string.Format("{0}[{1}]", x, idx2)),
				MemberName = context.MemberName.IfNotNull(x => string.Format("{0}[{1}]", x, idx2))
			};
		}

		protected override ValidationResult IsValid(object value, ValidationContext context)
		{
			if (value == null)
			{
				return ValidationResult.Success;
			}

			CheckTargetType(value, context.MemberName);

			var member = GetMember(Property);

			if (member == null)
			{
				throw new ValidationException("Metadata Object of type {0} is missing property: {1}".Format(MetadataType.FullName as object, Property));
			}

			var attributes = member.GetCustomAttributes<ValidationAttribute>(true);
			var results = new List<ValidationResult>();

			if (attributes.Any())
			{
				var idx = 0;
				var valueAsEnumerable = GetEnumerable(value);
				foreach (var item in valueAsEnumerable)
				{
					var idx2 = idx++;

					// RequiredAttribute needs special treatment, as we might need to evaluate it against null (or default) values..
					var req = member.GetCustomAttribute<RequiredAttribute>();
					if (req != null)
					{
						var tmp = CreateContextFor(item ?? "", context, idx2);
						var res = req.GetValidationResult(item, tmp);
						if (res != ValidationResult.Success) results.Add(res);
					}

					if (item == null) continue;

					var newctx = new ValidationContext(item, null, context.Items)
					{
						DisplayName = context.DisplayName.IfNotNull(x => string.Format("{0}[{1}]", x, idx2)),
						MemberName = context.MemberName.IfNotNull(x => string.Format("{0}[{1}]", x, idx2))
					};
					results.AddRange(ValidateClass(item, newctx, attributes));
				}
			}

			return results.Count == 0 ?
				ValidationResult.Success :
				AggregateValidationResult.CreateFor(
					FormatErrorMessage(context.DisplayName.IsNullOrEmpty() ? context.MemberName : context.DisplayName), results);
		}
	}
}
#endif
