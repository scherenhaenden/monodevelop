// SyntaxModeService.cs
//
// Author:
//   Mike Krüger <mkrueger@novell.com>
//
// Copyright (c) 2007 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Xml;
using System.Xml.Schema;
using System.Linq;
using Mono.Addins;
using MonoDevelop.Core;
using MonoDevelop.Core.Text;
using MonoDevelop.Components;

namespace MonoDevelop.Ide.Editor.Highlighting
{
	public static class SyntaxModeService
	{
		static Dictionary<string, EditorTheme> styles          = new Dictionary<string, EditorTheme> ();
		static Dictionary<string, IStreamProvider> styleLookup = new Dictionary<string, IStreamProvider> ();
		static List<SyntaxHighlightingDefinition> highlightings = new List<SyntaxHighlightingDefinition> ();

		public static string[] Styles {
			get {
				List<string> result = new List<string> ();
				foreach (string style in styles.Keys) {
					if (!result.Contains (style))
						result.Add (style);
				}
				foreach (string style in styleLookup.Keys) {
					if (!result.Contains (style))
						result.Add (style);
				}
				return result.ToArray ();
			}
		}

		public static EditorTheme DefaultColorStyle {
			get {
				return GetEditorTheme (EditorTheme.DefaultColorStyle);
			}
		}

		public static EditorTheme GetDefaultColorStyle (this Theme theme)
		{
			return GetEditorTheme (GetDefaultColorStyleName (theme));
		}

		public static string GetDefaultColorStyleName ()
		{
			return GetDefaultColorStyleName (IdeApp.Preferences.UserInterfaceTheme);
		}

		public static string GetDefaultColorStyleName (this Theme theme)
		{
			switch (theme) {
				case Theme.Light:
					return IdePreferences.DefaultLightColorScheme;
				case Theme.Dark:
					return IdePreferences.DefaultDarkColorScheme;
				default:
					throw new InvalidOperationException ();
			}
		}

		public static EditorTheme GetUserColorStyle (this Theme theme)
		{
			var schemeName = IdeApp.Preferences.ColorScheme.ValueForTheme (theme);
			return GetEditorTheme (schemeName);
		}

		public static bool FitsIdeTheme (this EditorTheme editorTheme, Theme theme)
		{
			Components.HslColor bgColor;
			editorTheme.TryGetColor (ThemeSettingColors.Background, out bgColor);
			if (theme == Theme.Dark)
				return (bgColor.L <= 0.5);
			return (bgColor.L > 0.5);
		}

		public static EditorTheme GetEditorTheme (string name)
		{
			if (styleLookup.ContainsKey (name)) {
				LoadStyle (name);
			}
			if (!styles.ContainsKey (name)) {
				LoggingService.LogWarning ("Color style " + name + " not found, switching to default.");
				name = GetDefaultColorStyleName ();
			}
			if (!styles.ContainsKey (name)) {
				LoggingService.LogError ("Color style " + name + " not found.");
				return null;
			}
			return styles [name];
		}

		static IStreamProvider GetProvider (EditorTheme style)
		{
			if (styleLookup.ContainsKey (style.Name)) 
				return styleLookup[style.Name];
			return null;
		}
		
		static void LoadStyle (string name)
		{
			if (!styleLookup.ContainsKey (name))
				throw new System.ArgumentException ("Style " + name + " not found", "name");
			var provider = styleLookup [name];
			styleLookup.Remove (name); 
			var stream = provider.Open ();
			try {
				if (provider is UrlStreamProvider) {
					var usp = provider as UrlStreamProvider;
					if (usp.Url.EndsWith (".vssettings", StringComparison.Ordinal)) {
						// TODO: Write vssettings -> tmTheme converter
						LoggingService.LogWarning ("VS.NET styles no longer supported.");
					} else {
						styles [name] = TextMateFormat.LoadEditorTheme (stream);
					}
					styles [name].FileName = usp.Url;
				} else {
					styles [name] = TextMateFormat.LoadEditorTheme (stream);
				}
			} catch (Exception e) {
				LoggingService.LogError ("Error while loading style :" + name, e);
				throw new IOException ("Error while loading style :" + name, e);
			} finally {
				stream.Close ();
			}
		}
		

		internal static void Remove (EditorTheme style)
		{
			if (styleLookup.ContainsKey (style.Name))
				styleLookup.Remove (style.Name);

			foreach (var kv in styles) {
				if (kv.Value == style) {
					styles.Remove (kv.Key); 
					return;
				}
			}
		}
		

		static List<ValidationEventArgs> ValidateStyleFile (string fileName)
		{
			List<ValidationEventArgs> result = new List<ValidationEventArgs> ();
			return result;
		}


		internal static void LoadStylesAndModes (string path)
		{
			foreach (string file in Directory.GetFiles (path)) {
				if (file.EndsWith (".json", StringComparison.Ordinal)) {
					using (var stream = File.OpenRead (file)) {
						string styleName = ScanStyle (stream);
						if (!string.IsNullOrEmpty (styleName)) {
							styleLookup [styleName] = new UrlStreamProvider (file);
						} else {
							Console.WriteLine ("Invalid .json syntax sheme file : " + file);
						}
					}
				} else if (file.EndsWith (".vssettings", StringComparison.Ordinal)) {
					using (var stream = File.OpenRead (file)) {
						string styleName = Path.GetFileNameWithoutExtension (file);
						styleLookup [styleName] = new UrlStreamProvider (file);
					}
				} 
			}
		}

		static void LoadStylesAndModes (Assembly assembly)
		{
			foreach (string resource in assembly.GetManifestResourceNames ()) {
				if (resource.EndsWith (".tmTheme", StringComparison.Ordinal)) {
					using (Stream stream = assembly.GetManifestResourceStream (resource)) {
						string styleName = ScanStyle (stream);
						styleLookup [styleName] = new ResourceStreamProvider (assembly, resource);
					}
					continue;
				}
				if (resource.EndsWith (".sublime-package", StringComparison.Ordinal)) {
					using (var stream = new ICSharpCode.SharpZipLib.Zip.ZipInputStream (assembly.GetManifestResourceStream (resource))) {
						var entry = stream.GetNextEntry ();
						while (entry != null) {
							if (entry.IsFile && !entry.IsCrypted && entry.Name.EndsWith (".sublime-syntax", StringComparison.Ordinal)) {
								if (stream.CanDecompressEntry) {
									byte [] data = new byte [entry.Size];
									stream.Read (data, 0, (int)entry.Size);
									using (var tr = new StringReader (TextFileUtility.GetText (data))) {
										var highlighting = Sublime3Format.ReadHighlighting (tr);
										if (highlighting != null)
											highlightings.Add (highlighting);
									}
								}
							}
							entry = stream.GetNextEntry ();
						}
					}
				}
			}
		}

		static System.Text.RegularExpressions.Regex nameRegex = new System.Text.RegularExpressions.Regex ("\\<string\\>(.*)\\<\\/string\\>");

		static string ScanStyle (Stream stream)
		{
			try {
				var file = TextFileUtility.OpenStream (stream);
				string keyString = "<key>name</key>";
				while (true) {
					var line = file.ReadLine ();
					if (line == null)
						return "";
					if (line.Contains (keyString))
						break;
				}

				var nameLine = file.ReadLine ();
				file.Close ();
				var match = nameRegex.Match (nameLine);
				if (!match.Success)
					return null;
				return match.Groups[1].Value;
			} catch (Exception e) {
				Console.WriteLine ("Error while scanning json:");
				Console.WriteLine (e);
				return null;
			}
		}

		internal static void AddStyle (EditorTheme style)
		{
			styles [style.Name] = style;
		}

		internal static void AddStyle (IStreamProvider provider)
		{
			using (var stream = provider.Open ()) {
				string styleName = ScanStyle (stream);
				styleLookup [styleName] = provider;
			}
		}

		internal static void RemoveStyle (IStreamProvider provider)
		{
			using (var stream = provider.Open ()) {
				string styleName = ScanStyle (stream);
				styleLookup.Remove (styleName);
			}
		}

		static SyntaxModeService ()
		{
			LoadStylesAndModes (typeof (SyntaxModeService).Assembly);
			var textEditorAssembly = Assembly.Load ("MonoDevelop.SourceEditor");
			if (textEditorAssembly != null) {
				LoadStylesAndModes (textEditorAssembly);
			} else {
				LoggingService.LogError ("Can't lookup Mono.TextEditor assembly. Default styles won't be loaded.");
			}
		}

		public static HslColor GetColor (EditorTheme style, string key)
		{
			HslColor result;
			if (!style.TryGetColor (key, out result))
				DefaultColorStyle.TryGetColor (key, out result);
			return result;
		}

		internal static ChunkStyle GetChunkStyle (EditorTheme style, string key)
		{
			HslColor result;
			if (!style.TryGetColor (key, out result))
				DefaultColorStyle.TryGetColor (key, out result);
			return new ChunkStyle() { Foreground = result };
		}


		internal static SyntaxHighlightingDefinition GetSyntaxHighlightingDefinition (FilePath fileName, string mimeType)
		{
			var ext = fileName.Extension;
			foreach (var h in highlightings) {
				if (h.FileExtensions.Contains (ext))
					return h;
				foreach (var fe in h.FileExtensions) {
					var mime = DesktopService.GetMimeTypeForUri ("a." + fe);
					if (mimeType == mime)
						return h;
				}
			}
			return null;
		}
	}
}