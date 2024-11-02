using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

/// <summary>Code analyser displaying classes that are overloaded with code</summary>
class CodeOverview : EditorWindow
{
	const string COMMENT_FLAG = "//";
	const string EDITOR_FLAG = "using UnityEditor;";
	const string CONFIG_FILE = "CodeOverviewConfig.json";

	string scriptName => GetType().Name + ".cs";

	GUIStyle boldCenterGreyStyle;
	GUIStyle boldCenterStyle;
	GUIStyle boldGreyStyle;
	GUIStyle buttonStyle;
	GUIStyle mainFrame;

	List<Script> mediumScripts;
	List<Script> badScripts;
	List<TextAsset> allScripts;
	List<Type> projectTypes;
	CodeOverview window;
	Config config;
	Color goodColor;
	Color mediumColor;
	Color badColor;
	Vector2 scrollPos;
	Vector2 excludeScroll;
	Vector2 mediumScroll;
	Vector2 badScroll;
	float averageLineCount;
	int scriptsCount;
	int classCount;
	int editorScriptsCount;
	int monoBehavioursCount;
	int nonMonoBehavioursCount;
	int interfacesCount;
	int totalLineCount;
	int selectedScriptIndex;

	[MenuItem("Tools/CodeOverview")]
	static void ShowWindow()
	{
		CodeOverview codeOverview = GetWindow<CodeOverview>();
		codeOverview.titleContent = new GUIContent("Code Overview");

		codeOverview.LoadConfig();
		codeOverview.LoadProjectData();
		codeOverview.ScanProjectScripts();

		codeOverview.window = codeOverview;
		codeOverview.Show();
	}

	void GenerateRequesites()
	{
		if (boldCenterGreyStyle == null)
		{
			boldCenterGreyStyle = new GUIStyle(GUI.skin.label)
			{
				alignment = TextAnchor.MiddleCenter,
				fontStyle = FontStyle.Bold,
				normal = new GUIStyleState() { textColor = Color.grey }
			};
		}

		if (boldCenterStyle == null)
		{
			boldCenterStyle = new GUIStyle(GUI.skin.label)
			{
				alignment = TextAnchor.MiddleCenter,
				fontStyle = FontStyle.Bold
			};
		}

		if (boldGreyStyle == null)
		{
			boldGreyStyle = new GUIStyle(GUI.skin.label)
			{
				fontStyle = FontStyle.Bold,
				normal = new GUIStyleState() { textColor = Color.grey }
			};
		}

		if (buttonStyle == null)
		{
			buttonStyle = new GUIStyle(GUI.skin.box)
			{
				stretchWidth = true,
				alignment = TextAnchor.MiddleCenter,
				richText = true
			};
		}

		if (mainFrame == null)
			mainFrame = new GUIStyle(GUI.skin.box) { stretchWidth = true };

		if (mediumScripts == null)
			mediumScripts = new List<Script>();

		if (badScripts == null)
			badScripts = new List<Script>();

		if (allScripts == null)
			allScripts = new List<TextAsset>();

		if (projectTypes == null)
			projectTypes = new List<Type>();

		goodColor = Color.green;
		mediumColor = new Color(0.75f, 0.5f, 0);
		badColor = Color.red;

		if (config == null)
			LoadConfig();
	}

	void OnGUI()
	{
		GenerateRequesites();

		EditorGUILayout.LabelField("Code Overview", boldCenterStyle);

		EditorGUILayout.Space();

		scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUIStyle.none, GUI.skin.verticalScrollbar);
		{
			EditorGUILayout.BeginVertical(mainFrame);
			{
				EditorGUILayout.LabelField("Statistics", boldCenterGreyStyle);
				EditorGUILayout.LabelField("Scripts count : " + scriptsCount);

				EditorGUILayout.Space();

				EditorGUILayout.LabelField("Total classes count : " + classCount);
				EditorGUILayout.LabelField("Editor scripts count : " + editorScriptsCount);
				EditorGUILayout.LabelField("MonoBehaviours count : " + monoBehavioursCount);
				EditorGUILayout.LabelField("Non-MonoBehaviours count : " + nonMonoBehavioursCount);
				EditorGUILayout.LabelField("Intefaces count : " + interfacesCount);

				EditorGUILayout.Space();

				EditorGUILayout.LabelField("Total lines count : " + totalLineCount);

				string averageLineString = averageLineCount.ToString();

				if (averageLineString.Contains(","))
				{
					if (averageLineString[averageLineString.Length - 1] != '0')
						averageLineString = Mathf.FloorToInt(averageLineCount).ToString();
					else
					{
						int totalCount = 0;
						string[] fragments = averageLineCount.ToString().Split(',');
						totalCount = fragments[0].Length + 2;
						averageLineString = averageLineCount.ToString().Substring(0, totalCount);
					}
				}

				Color averageColor = badColor;

				if (averageLineCount <= config.goodThreshold)
					averageColor = goodColor;
				else if (averageLineCount <= config.mediumThreshold)
					averageColor = mediumColor;

				averageColor -= Color.grey / 2; // dim color

				EditorGUILayout.LabelField(
					"Average line count: <color=#" +
					ColorUtility.ToHtmlStringRGB(averageColor) +
					">" + averageLineString + "</color>",
					new GUIStyle(GUI.skin.label) { richText = true }
				);
			}
			EditorGUILayout.EndVertical();

			EditorGUILayout.Space();

			EditorGUILayout.BeginVertical(mainFrame);
			{
				EditorGUILayout.LabelField("Settings", boldCenterGreyStyle);
				EditorGUILayout.Space();

				int previousGood = config.goodThreshold;
				int previousMedium = config.mediumThreshold;

				config.goodThreshold = EditorGUILayout.IntField("Good threshold", config.goodThreshold);
				config.mediumThreshold = EditorGUILayout.IntField("Medium threshold", config.mediumThreshold);

				if (previousGood != config.goodThreshold || previousMedium != config.mediumThreshold)
				{
					SaveConfig();

					LoadProjectData();
					ScanProjectScripts();
				}

				EditorGUILayout.Space();

				excludeScroll = EditorGUILayout.BeginScrollView(excludeScroll, GUILayout.MaxHeight(150));
				{
					List<string> toRemove = new List<string>();

					foreach (string script in config.excludedScripts)
					{
						EditorGUILayout.BeginHorizontal();
						{
							EditorGUILayout.LabelField(script, boldGreyStyle);

							if (GUILayout.Button("Remove"))
								toRemove.Add(script);
						}
						EditorGUILayout.EndHorizontal();
					}

					if (toRemove.Count > 0)
					{
						toRemove.ForEach(script => config.excludedScripts.Remove(script));
						SaveConfig();

						LoadProjectData();
						ScanProjectScripts();
					}
				}
				EditorGUILayout.EndScrollView();

				EditorGUILayout.Space();

				EditorGUILayout.BeginHorizontal();
				{
					List<string> fileNames = new List<string>();

					allScripts.ForEach(script =>
					{
						if (!config.excludedScripts.Contains(script.name))
							fileNames.Add(script.name);
					});

					selectedScriptIndex = EditorGUILayout.Popup(selectedScriptIndex, fileNames.ToArray());

					if (GUILayout.Button("Add exception"))
					{
						config.excludedScripts.Add(fileNames[selectedScriptIndex]);
						selectedScriptIndex = 0;

						SaveConfig();

						LoadProjectData();
						ScanProjectScripts();
					}
				}
				EditorGUILayout.EndHorizontal();
			}
			EditorGUILayout.EndVertical();

			EditorGUILayout.Space();

			EditorGUILayout.BeginVertical(mainFrame, GUILayout.MinHeight(120));
			{
				EditorGUILayout.LabelField("Scripts", boldCenterGreyStyle);

				float columnSize = window.position.width / 2 - 6;

				EditorGUILayout.BeginHorizontal();
				{
					EditorGUILayout.BeginVertical(GUILayout.Width(columnSize));
					{
						mediumScroll = EditorGUILayout.BeginScrollView(
							mediumScroll,
							GUIStyle.none,
							GUI.skin.verticalScrollbar
						);
						{
							ShowScripts(mediumScripts, mediumColor);
						}
						EditorGUILayout.EndScrollView();
					}
					EditorGUILayout.EndVertical();

					EditorGUILayout.BeginVertical(GUILayout.Width(columnSize));
					{
						badScroll = EditorGUILayout.BeginScrollView(
							badScroll,
							GUIStyle.none,
							GUI.skin.verticalScrollbar
						);
						{
							ShowScripts(badScripts, badColor);
						}
						EditorGUILayout.EndScrollView();
					}
					EditorGUILayout.EndVertical();
				}
				EditorGUILayout.EndHorizontal();
			}
			EditorGUILayout.EndVertical();

			EditorGUILayout.Space();

			EditorGUILayout.BeginHorizontal();
			{
				EditorGUILayout.Space();

				if (GUILayout.Button("Refresh"))
				{
					LoadProjectData();
					ScanProjectScripts();
				}

				EditorGUILayout.Space();
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space();
		}
		EditorGUILayout.EndScrollView();
	}

	string GetConfigPath()
	{
		string[] paths = Directory.GetFiles(Application.dataPath, scriptName, SearchOption.AllDirectories);

		if (paths.Length == 0)
		{
			Debug.LogError("Couldn't find path for current script");
			return null;
		}

		return paths[0].Replace(scriptName, CONFIG_FILE);
	}

	void LoadConfig()
	{
		string projectConfigPath = GetConfigPath().Replace(Application.dataPath, "Assets/");
		TextAsset configFile = AssetDatabase.LoadMainAssetAtPath(projectConfigPath) as TextAsset;

		if (configFile == null)
			config = new Config();
		else
			config = JsonUtility.FromJson<Config>(configFile.text);
	}

	void SaveConfig()
	{
		if (config == null) return;

		string json = JsonUtility.ToJson(config, true);
		File.WriteAllText(GetConfigPath(), json);
		AssetDatabase.ImportAsset(GetConfigPath());
	}

	void ScanProjectScripts()
	{
		// script reading part
		scriptsCount = allScripts.Count;
		editorScriptsCount = 0;
		totalLineCount = 0;

		mediumScripts = new List<Script>();
		badScripts = new List<Script>();

		if (allScripts.Count != 0)
		{
			// scan scripts
			foreach (TextAsset script in allScripts)
			{
				string[] fileLines = script.text.Split('\n');
				int classLineCount = 0;

				foreach (string line in fileLines)
				{
					if (!string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith(COMMENT_FLAG))
						classLineCount++;
				}

				totalLineCount += classLineCount;

				// count editor script
				if (script.text.Contains(EDITOR_FLAG))
					editorScriptsCount++;

				// skip this script
				if (config.excludedScripts.Contains(script.name))
					continue;

				// gets bad and medium files
				if (classLineCount >= config.mediumThreshold)
					badScripts.Add(new Script(script, classLineCount));
				else if (classLineCount >= config.goodThreshold)
					mediumScripts.Add(new Script(script, classLineCount));
			}

			// count average lines
			averageLineCount = (float)totalLineCount / scriptsCount;
		}

		// reflectivity part
		classCount = 0;
		monoBehavioursCount = 0;
		nonMonoBehavioursCount = 0;
		interfacesCount = 0;

		foreach (Type type in projectTypes)
		{
			if (type.IsInterface)
				interfacesCount++;

			if (type.IsClass)
			{
				classCount++;

				if (InheritsMonoBehaviour(type))
					monoBehavioursCount++;
				else
					nonMonoBehavioursCount++;
			}
		}
	}

	bool InheritsMonoBehaviour(Type type)
	{
		while (type != null)
		{
			if (type.BaseType == typeof(MonoBehaviour))
				return true;
			else
				type = type.BaseType;
		}

		return false;
	}

	void ShowScripts(List<Script> listToShow, Color color)
	{
		listToShow.Sort();

		foreach (Script script in listToShow)
		{
			if (GUILayout.Button(script.ToString(color), buttonStyle))
				script.OpenInIDE();
		}
	}

	void LoadProjectData()
	{
		// load all scripts
		string[] assetsPaths = AssetDatabase.GetAllAssetPaths();
		allScripts = new List<TextAsset>();

		foreach (string assetPath in assetsPaths)
		{
			if (!assetPath.Contains("Package") && !assetPath.Contains("Plugins") && assetPath.EndsWith(".cs"))
				allScripts.Add(AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath));
		}

		// load types
		projectTypes = new List<Type>();

		List<Assembly> projectAssemblies = new List<Assembly>(
			AppDomain.CurrentDomain.GetAssemblies()).FindAll(asmdef => asmdef.FullName.Contains("Assembly-CSharp")
		);

		foreach (Assembly assembly in projectAssemblies)
		{
			foreach (Type type in assembly.GetTypes())
			{
				if (type.GetCustomAttribute<CompilerGeneratedAttribute>() == null)
					projectTypes.Add(type);
			}
		}
	}

	/// <summary>Lightweight class to keep infos of classes</summary>
	struct Script : IComparable
	{
		public int lineCount;
		public string fileName;
		public Action OpenInIDE;

		public Script(TextAsset script, int lineCount)
		{
			this.lineCount = lineCount;
			fileName = script.name;
			OpenInIDE = () => AssetDatabase.OpenAsset(script);
		}

		public string ToString(Color color)
		{
			return "<b>" + fileName + "</b> " +
				"(<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" +
				lineCount + "</color>)";
		}

		int IComparable.CompareTo(object obj)
		{
			if (obj is Script)
			{
				Script other = (Script)obj;
				return lineCount.CompareTo(other.lineCount);
			}
			else
				return 0;
		}
	}

	/// <summary>Class to store user preferences</summary>
	[Serializable]
	class Config
	{
		public int goodThreshold;
		public int mediumThreshold;
		public List<string> excludedScripts;

		public Config()
		{
			goodThreshold = 150;
			mediumThreshold = 300;

			excludedScripts = new List<string>();
		}
	}
}