using System.ComponentModel.DataAnnotations;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace Pathoschild.ModData.Common;

/// <summary>Handles loading configuration files.</summary>
public static class ConfigHelper
{
	/*********
	** Public methods
	*********/
	/// <summary>Load configuration from the current application's <c>appsettings</c> files.</summary>
	/// <typeparam name="TModel">The settings model type.</typeparam>
	/// <exception cref="FileNotFoundException">No <c>appsettings.jsonc</c> or <c>appsettings.local.json</c> file was found.</exception>
	/// <exception cref="ValidationException">The validation annotations on the model weren't satisfied by the loaded settings.</exception>
	public static TModel LoadAppSettings<TModel>()
	{
		ConfigurationBuilder configurationBuilder = new();

		TModel? config = configurationBuilder
			.AddJsonFile("appsettings.jsonc", optional: false)
			.AddJsonFile("appsettings.local.jsonc", optional: true)
			.Build()
			.Get<TModel>();

		if (config is null)
			throw new FileNotFoundException("Can't load settings from `appsettings.jsonc` or `appsettings.local.jsonc`. Does the file exist?");

		Validator.ValidateObject(config, new ValidationContext(config), validateAllProperties: true);

		return config;
	}
}
