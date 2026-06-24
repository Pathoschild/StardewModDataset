using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Nodes;
using Newtonsoft.Json;
using StardewModdingAPI.Toolkit.Serialization;

namespace Pathoschild.ModData.FetchMods.Framework;

/// <summary>Provides utility methods for transforming data.</summary>
internal static class DataHelper
{
	/*********
	** Public methods
	*********/
	/// <summary>Get a raw data dictionary of an object's property values.</summary>
	/// <param name="data">The input data to clone.</param>
	/// <param name="excludeFields">The fields to exclude from the new dictionary.</param>
	/// <returns>Returns a dictionary of raw properties, or <c>null</c> if none were found.</returns>
	public static Dictionary<string, JsonNode?>? PropertiesToRawData(object? data, HashSet<string> excludeFields)
	{
		if (data is null)
			return null;

		Type dataType = data.GetType();

		Dictionary<string, JsonNode?>? clone = null;

		foreach (PropertyInfo property in dataType.GetProperties())
		{
			object? rawValue = property.GetValue(data);
			TryAddMember(property, property.PropertyType, rawValue);
		}

		foreach (FieldInfo field in dataType.GetFields())
		{
			object? rawValue = field.GetValue(data);
			TryAddMember(field, field.FieldType, rawValue);
		}

		return clone;

		void TryAddMember(MemberInfo member, Type memberType, object? rawValue)
		{
			// handle extension data
			if (memberType == typeof(Dictionary<string, object>) && member.GetCustomAttribute<JsonExtensionDataAttribute>() != null)
			{
				if (rawValue is Dictionary<string, object> fields)
				{
					foreach ((string fieldName, object fieldValue) in fields)
					{
						if (excludeFields.Contains(fieldName))
							continue;

						clone ??= [];
						clone[fieldName] = JsonHelper.ConvertToSystemTextJsonNode(fieldValue);
					}
				}
				return;
			}

			// handle field/property directly
			if (!excludeFields.Contains(member.Name))
			{
				clone ??= [];
				clone[member.Name] = JsonHelper.ConvertToSystemTextJsonNode(rawValue);
			}
		}
	}
}
