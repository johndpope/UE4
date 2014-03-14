// Copyright 1998-2014 Epic Games, Inc. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Reflection;
using Microsoft.Win32;
using System.Linq;
using System.Diagnostics;

namespace UnrealBuildTool
{
	/// <summary>
	/// Represents a folder within the master project (e.g. Visual Studio solution)
	/// </summary>
	public abstract class MasterProjectFolder
	{
		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="InitOwnerProjectFileGenerator">Project file generator that owns this object</param>
		/// <param name="InitFolderName">Name for this folder</param>
		public MasterProjectFolder( ProjectFileGenerator InitOwnerProjectFileGenerator, string InitFolderName )
		{
			OwnerProjectFileGenerator = InitOwnerProjectFileGenerator;
			FolderName = InitFolderName;
		}

		/// Name of this folder
		public string FolderName
		{
			get;
			private set;
		}


		/// <summary>
		/// Adds a new sub-folder to this folder
		/// </summary>
		/// <param name="SubFolderName">Name of the new folder</param>
		/// <returns>The newly-added folder</returns>
		public MasterProjectFolder AddSubFolder( string SubFolderName )
		{
			foreach( var CurFolder in SubFolders )
			{
				if( CurFolder.FolderName.Equals( SubFolderName, StringComparison.InvariantCultureIgnoreCase ) )
				{
					// Already exists!
					return CurFolder;
				}
			}
			var NewFolder = OwnerProjectFileGenerator.AllocateMasterProjectFolder( OwnerProjectFileGenerator, SubFolderName );
			SubFolders.Add( NewFolder );
			return NewFolder;
		}


		/// <summary>
		/// Recursively searches for the specified project and returns the folder that it lives in, or null if not found
		/// </summary>
		/// <param name="Project">The project file to look for</param>
		/// <returns>The found folder that the project is in, or null</returns>
		public MasterProjectFolder FindFolderForProject( ProjectFile Project )
		{
			foreach( var CurFolder in SubFolders )
			{
				MasterProjectFolder FoundFolder = CurFolder.FindFolderForProject( Project );
				if( FoundFolder != null )
				{
					return FoundFolder;
				}
			}

			foreach( var ChildProject in ChildProjects )
			{
				if( ChildProject == Project )
				{
					return this;
				}
			}

			return null;
		}

		/// Owner project generator
		readonly ProjectFileGenerator OwnerProjectFileGenerator;

		/// Sub-folders
		public readonly List<MasterProjectFolder> SubFolders = new List<MasterProjectFolder>();

		/// Child projects
		public readonly List<ProjectFile> ChildProjects = new List<ProjectFile>();

		/// Files in this folder.  These are files that aren't part of any project, but display in the IDE under the project folder
		/// and can be browsed/opened by the user easily in the user interface
		public readonly List<string> Files = new List<string>();		
	}


	/// <summary>
	/// Base class for all project file generators
	/// </summary>
	public abstract class ProjectFileGenerator
	{
		/// Global static that enables generation of project files.  Doesn't actually compile anything.
		/// This is enabled only via UnrealBuildTool command-line.
		public static bool bGenerateProjectFiles = false;

		/// True if we're generating lightweight project files for a single game only, excluding most engine code, documentation, etc.
		public bool bGeneratingGameProjectFiles = false;

		/// True if we're generating project files to be used with Rocket
		public static bool bGeneratingRocketProjectFiles = false;

		/// Optional list of platforms to generate projects for
		readonly List<UnrealTargetPlatform> ProjectPlatforms = new List<UnrealTargetPlatform>();

		/// Whether engine module projects should be included in single-game projects, or Rocket projects.  This is useful if you want to be able to
		/// quickly locate engine header files using VAX, for example.  But it clutters up the solution file.
		protected bool bAlwaysIncludeEngineModules = false;

		/// When bGeneratingGameProjectFiles=true, this is the game name we're generating projects for
		protected string GameProjectName = null;

		/// Global static that only adds platforms that are supported when generating a given target.
		/// This was the old behavior, and it resulted in scenarios where having an unsupported platform selected
		/// in the platform drop-down would silently 'switch' to building Win32.
		/// The new behavior is to add all platforms when generating a target, and then check if it is supported
		/// at build time. If it is not, then a BuildException is thrown informing the user of an unsupported platform.
		/// NOTE: This only matters when using "-AllProjects".  It can increase the project file load times though, because of all
		///       of the extra project configuration combinations we need to store
		public static bool bCreateDummyConfigsForUnsupportedPlatforms = true;

		/// Whether we should include configurations for "Test" and "Shipping" in generated projects (pass "-NoShippingConfigs" to disable this)
		/// NOTE: This is currently ignored when generating Rocket projects
		public static bool bIncludeTestAndShippingConfigs = true;

		/// True if intellisense data should be generated (takes a while longer)
		bool bGenerateIntelliSenseData = true;

		/// True if we should include documentation in the generated projects
		bool IncludeDocumentation = true;

		/// True if all documentation languages should be included in generated projects, otherwise only "INT" will be included
		bool bAllDocumentationLanguages = false;

		/// True if shader source files should be included in the generated projects
		bool IncludeShaderSource = true;

		/// True if build system files should be included
		bool IncludeBuildSystemFiles = true;

		/// True if we should include config files (.ini files) in the generated project
		bool IncludeConfigFiles = true;

		/// True if we should include localization files (.int/.kor/etc files) in the generated project
		bool IncludeLocalizationFiles = false;

        /// True if we should include template files (.template files) in the generated project
        bool IncludeTemplateFiles = true;

		/// True if we should reflect "Source" sub-directories on disk in the master project as master project directories.
		/// This arguably adds some visual clutter to the master project, but is truer to the on-disk file organization.
		bool KeepSourceSubDirectories = true;

		/// Relative path to the root of the engine/games (e.g. the directory above "Engine" and any sibling game directories)
		public static readonly string RootRelativePath = ".." + Path.DirectorySeparatorChar + "..";	// Assume CWD is "<root>/Engine/Source"

		/// Relative path from the CWD to the engine directory
		public static readonly string EngineRelativePath = Path.Combine( RootRelativePath, "Engine" );

		/// Relative path to the directory where the master project file will be saved to
		public static string MasterProjectRelativePath = RootRelativePath;		// We'll save the master project to our "root" folder

		/// Name of the UE4 engine project name that contains all of the engine code, config files and other files
		static readonly string EngineProjectFileNameBase = "UE4";

		/// When ProjectsAreIntermediate is true, this is the directory to store generated project files
		// @todo projectfiles: Ideally, projects for game modules/targets would be created in the game's Intermediate folder!
		public static string IntermediateProjectFilesPath = Path.Combine( EngineRelativePath, "Intermediate", "ProjectFiles" );

		/// Global static new line string used by ProjectFileGenerator to generate project files.
		public static readonly string NewLine = Environment.NewLine;

		/// If true, any module or source file under a NoRedist folder will not be included in the generated projects
		protected bool bExcludeNoRedistFiles = false;

		/// If true, we'll parse subdirectories of third-party projects to locate source and header files to include in the
		/// generated projects.  This can make the generated projects quite a bit bigger, but makes it easier to open files
		/// directly from the IDE.
		bool bGatherThirdPartySource = false;

		/// Name of the master project file (e.g. base file name for the solution file for Visual Studio, or the Xcode project file on Mac)
		protected string MasterProjectName = "UE4";

		/// Maps all module names that were included in generated project files, to actual project file objects.
		/// @todo projectfiles: Nasty global static list.  This is only really used for IntelliSense, and to avoid extra folder searches for projects we've already cached source files for.
		public static readonly Dictionary<string, ProjectFile> ModuleToProjectFileMap = new Dictionary<string, ProjectFile>( StringComparer.InvariantCultureIgnoreCase );

	
		/// File extension for project files we'll be generating (e.g. ".vcxproj")
		abstract public string ProjectFileExtension
		{
			get;
		}

		/// True if we should include IntelliSense data in the generated project files when possible
		virtual public bool ShouldGenerateIntelliSenseData()
		{
			return bGenerateIntelliSenseData;
		}

		/// <summary>
		/// Adds all .automation.csproj files to the solution.
		/// </summary>
		void AddAutomationModules(MasterProjectFolder ProgramsFolder)
		{
			var Folder = ProgramsFolder.AddSubFolder("Automation");
			var AllGameFolders = UEBuildTarget.DiscoverAllGameFolders();
			var BuildFolders = new List<string>(AllGameFolders.Count);
			foreach (var GameFolder in AllGameFolders)
			{
				var GameBuildFolder = Path.Combine(GameFolder, "Build");
				if (Directory.Exists(GameBuildFolder))
				{
					BuildFolders.Add(GameBuildFolder);
				}
			}

			// Find all the automation modules .csproj files to add
			var ModuleFiles = RulesCompiler.FindAllRulesSourceFiles(RulesCompiler.RulesFileType.AutomationModule, BuildFolders);
			foreach (var ProjectFile in ModuleFiles)
			{
				FileInfo Info = new FileInfo(Path.Combine(ProjectFile));
				if (Info.Exists)
				{
					var RelativeFileName = Utils.MakePathRelativeTo(ProjectFile, MasterProjectRelativePath);
					var Project = new VCSharpProjectFile(RelativeFileName);
					AddExistingProjectFile(Project);

					Folder.ChildProjects.Add( Project );
				}
			}
		}

		/// <summary>
		/// Generates a Visual Studio solution file and Visual C++ project files for all known engine and game targets.
		/// Does not actually build anything.
		/// </summary>
		/// <param name="Arguments">Command-line arguments</param>
		/// <param name="bSuccess">True if everything went OK</param>
		public void GenerateProjectFiles( String[] Arguments, out bool bSuccess )
		{
			bSuccess = true;

			// Parse project generator options
			bool IncludeAllPlatforms = true;
			ConfigureProjectFileGeneration( Arguments, ref IncludeAllPlatforms);

			if( bGeneratingGameProjectFiles )
			{
				Log.TraceInformation("Discovering modules, targets and source code for game...");

				MasterProjectRelativePath = UnrealBuildTool.GetUProjectPath();
					
					// Set the project file name
				MasterProjectName = Path.GetFileNameWithoutExtension(UnrealBuildTool.GetUProjectFile());

				if (!Directory.Exists(MasterProjectRelativePath + "/Source"))
				{
					if (ExternalExecution.GetRuntimePlatform () == UnrealTargetPlatform.Mac)
					{
						MasterProjectRelativePath = Path.GetFullPath(Path.Combine (Utils.GetExecutingAssemblyDirectory (), "..", "..", "..", "Engine"));
						GameProjectName = "UE4Game";
					}
					if (!Directory.Exists(MasterProjectRelativePath + "/Source"))
					{
						throw new BuildException ("Directory '{0}' is missing 'Source' folder.", MasterProjectRelativePath);
					}
				}
				IntermediateProjectFilesPath = Path.Combine(MasterProjectRelativePath, "Intermediate", "ProjectFiles");

				IncludeDocumentation = false;
				IncludeBuildSystemFiles = false;
				IncludeShaderSource = false;
				IncludeTemplateFiles = false;
				IncludeConfigFiles = true;
			}
			else if( bGeneratingRocketProjectFiles )
			{
				Log.TraceInformation("Discovering modules, targets and source code for project...");

				// NOTE: Realistically, the distro that the Rocket user is generating projects FROM won't have NoRedist files in it.  But when 
				//       testing from a developer branch, this is useful to get authentic projects. This only really matters when 
				//       bIncludeEngineModulesInRocketProjects=true (defaults to false.)				
				bExcludeNoRedistFiles = true;

				MasterProjectRelativePath = UnrealBuildTool.GetUProjectPath();
				IntermediateProjectFilesPath = Path.Combine( MasterProjectRelativePath, "Intermediate", "ProjectFiles" );

				// Set the project file name
				MasterProjectName = Path.GetFileNameWithoutExtension(UnrealBuildTool.GetUProjectFile());

				if (!Directory.Exists(MasterProjectRelativePath + "/Source"))
				{
					throw new BuildException("Directory '{0}' is missing 'Source' folder.", MasterProjectRelativePath);
				}

				IncludeDocumentation = false;
				IncludeBuildSystemFiles = false;
				IncludeShaderSource = false;
				IncludeTemplateFiles = false;
				IncludeConfigFiles = true;
			}

			// Modify the name if specific platforms were given
			if (ProjectPlatforms.Count > 0)
			{
				// Sort the platforms names so we get consistent names
				List<string> SortedPlatformNames = new List<string>();
				foreach (UnrealTargetPlatform SpecificPlatform in ProjectPlatforms)
				{
					SortedPlatformNames.Add(SpecificPlatform.ToString());
				}
				SortedPlatformNames.Sort();

				MasterProjectName += "_";
				foreach (string SortedPlatform in SortedPlatformNames)
				{
					MasterProjectName += SortedPlatform;
					IntermediateProjectFilesPath += SortedPlatform;
				}
			}

			bool bCleanProjectFiles = UnrealBuildTool.CommandLineContains( "-CleanProjects" );
			if (bCleanProjectFiles)
			{
				CleanProjectFiles(MasterProjectRelativePath, MasterProjectName, IntermediateProjectFilesPath);
			}

			// Figure out which platforms we should generate project files for.
			string SupportedPlatformNames;
			SetupSupportedPlatformsAndConfigurations( IncludeAllPlatforms:IncludeAllPlatforms, SupportedPlatformNames:out SupportedPlatformNames );

			Log.TraceVerbose( "Detected supported platforms: " + SupportedPlatformNames );

			RootFolder = AllocateMasterProjectFolder( this, "<Root>" );

			// Build the list of games to generate projects for
			var AllGameProjects = UProjectInfo.FilterGameProjects(true, bGeneratingGameProjectFiles ? GameProjectName : null);
				
			var AssemblyName = "ProjectFileGenerator";
			if( bGeneratingGameProjectFiles )
			{
				AssemblyName = GameProjectName + "ProjectFileGenerator";
			}
			else if( bGeneratingRocketProjectFiles )
			{
				AssemblyName = "RocketProjectFileGenerator";
			}

			List<string> AssemblyGameFolders = new List<string>();
			foreach (UProjectInfo Project in AllGameProjects)
			{
				AssemblyGameFolders.Add(Project.Folder);
			}
			RulesCompiler.SetAssemblyNameAndGameFolders( AssemblyName, AssemblyGameFolders );

			MasterProjectFolder ProgramsFolder = RootFolder;
			if( ( !bGeneratingGameProjectFiles && !bGeneratingRocketProjectFiles ) || bAlwaysIncludeEngineModules )
			{
				ProgramsFolder = RootFolder.AddSubFolder( "Programs" );
			}

			ProjectFile EngineProject = null;
			Dictionary<string, ProjectFile> GameProjects = null;
			Dictionary<string, ProjectFile> ProgramProjects = null;
			HashSet<ProjectFile> TemplateGameProjects = null;
			{
				// Setup buildable projects for all targets
				AddProjectsForAllTargets( AllGameProjects, out EngineProject, out GameProjects, out ProgramProjects, out TemplateGameProjects );

				// Add all game projects and game config files
				AddAllGameProjects(GameProjects, SupportedPlatformNames, RootFolder);

				// Place projects into root level solution folders
				if( ( !bGeneratingGameProjectFiles && !bGeneratingRocketProjectFiles ) || bAlwaysIncludeEngineModules )
				{
					if( EngineProject != null )
					{
						RootFolder.AddSubFolder( "Engine" ).ChildProjects.Add( EngineProject );

						// Engine config files
						if( IncludeConfigFiles )
						{
							AddEngineConfigFiles( EngineProject );
						}

						// Engine localization files
						if( IncludeLocalizationFiles )
						{
							AddEngineLocalizationFiles( EngineProject );
						}

						// Engine template files
						if (IncludeTemplateFiles)
						{
							AddEngineTemplateFiles( EngineProject );
						}

						if( IncludeShaderSource )
						{
							Log.TraceVerbose( "Adding shader source code..." );

							// Find shader source files and generate stub project
							AddEngineShaderSource( EngineProject );
						}

						if( IncludeBuildSystemFiles )
						{
							Log.TraceVerbose( "Adding build system files..." );

							AddEngineBuildFiles( EngineProject );
						}

						if( IncludeDocumentation )
						{
							AddEngineDocumentation( EngineProject );
						}
					}

					foreach( var CurGameProject in GameProjects.Values )
					{
						// Templates go under a different solution folder than games
						if( TemplateGameProjects.Contains( CurGameProject ) )
						{
							RootFolder.AddSubFolder( "Templates" ).ChildProjects.Add( CurGameProject );
						}
						else
						{
							RootFolder.AddSubFolder( "Games" ).ChildProjects.Add( CurGameProject );
						}
					}

					foreach( var CurProgramProject in ProgramProjects.Values )
					{
						ProgramsFolder.ChildProjects.Add( CurProgramProject );
					}

					// Add all of the config files for generated program targets
					AddEngineProgramConfigFiles( ProgramProjects );
				}
			}

			// Find all of the module files.  This will filter out any modules or targets that don't belong to platforms
			// we're generating project files for.
			var AllModuleFiles = DiscoverModules();

			// Setup "stub" projects for all modules
			AddProjectsForAllModules(AllGameProjects, ProgramProjects, AllModuleFiles, bGatherThirdPartySource);

			{
				if( !bGeneratingRocketProjectFiles )
				{
					// Add UnrealBuildTool to the master project
					AddUnrealBuildToolProject( ProgramsFolder );
				}

				if( ( !bGeneratingGameProjectFiles && !bGeneratingRocketProjectFiles ) || bAlwaysIncludeEngineModules )
				{
					// Add AutomationTool to the master project
					ProgramsFolder.ChildProjects.Add(AddSimpleCSharpProject("AutomationTool"));

					// Add UnrealAutomationTool (launcher) to the master project
					ProgramsFolder.ChildProjects.Add(AddSimpleCSharpProject("AutomationToolLauncher"));

					// Add automation.csproj files to the master project.
					AddAutomationModules(ProgramsFolder);

					// Add Distill to the master project
					ProgramsFolder.ChildProjects.Add( AddSimpleCSharpProject( "Distill" ) );

					// Add DotNETUtilities to the master project
					ProgramsFolder.ChildProjects.Add( AddSimpleCSharpProject(  "DotNETCommon/DotNETUtilities" ) );

					// Add all of the IOS C# projects
					AddIOSProjects( ProgramsFolder );

					// Add all of the Android C# projects
					AddAndroidProjects( ProgramsFolder );
				}


				// Eliminate all redundant master project folders.  E.g., folders which contain only one project and that project
				// has the same name as the folder itself.  To the user, projects "feel like" folders already in the IDE, so we
				// want to collapse them down where possible.
				EliminateRedundantMasterProjectSubFolders( RootFolder, "" );

	
				bool bWriteFileManifest = UnrealBuildTool.CommandLineContains("-filemanifest");

				if (bWriteFileManifest == false)
				{
					// Figure out which targets we need about IntelliSense for.  We only need to worry about targets for projects
					// that we're actually generating in this session.
					List<string> IntelliSenseTargetFiles = new List<string>();
					{
						// Engine targets
						if( EngineProject != null )
						{
							foreach( var ProjectTarget in EngineProject.ProjectTargets )
							{
								if( !String.IsNullOrEmpty( ProjectTarget.TargetFilePath ) )
								{
									IntelliSenseTargetFiles.Add( ProjectTarget.TargetFilePath );
								}
							}
						}

						// Program targets
						foreach( var ProgramProject in ProgramProjects.Values )
						{
							foreach( var ProjectTarget in ProgramProject.ProjectTargets )
							{
								if( !String.IsNullOrEmpty( ProjectTarget.TargetFilePath ) )
								{
									IntelliSenseTargetFiles.Add( ProjectTarget.TargetFilePath );
								}
							}
						}

						// Game/template targets
						foreach( var GameProject in GameProjects.Values )
						{
							foreach( var ProjectTarget in GameProject.ProjectTargets )
							{
								if( !String.IsNullOrEmpty( ProjectTarget.TargetFilePath ) )
								{
									IntelliSenseTargetFiles.Add( ProjectTarget.TargetFilePath );
								}
							}
						}
					}

					// Generate IntelliSense data if we need to.  This involves having UBT simulate the action compilation of
					// the targets so that we can extra the compiler defines, include paths, etc.
					bSuccess = GenerateIntelliSenseData(Arguments, IntelliSenseTargetFiles );
				}


				// If everything went OK, we'll now save out all of the new project files
				if( bSuccess )
				{
					if (bWriteFileManifest == false)
					{
						// Save new project files
						WriteProjectFiles();

						Log.TraceVerbose( "Project generation complete ({0} generated, {1} imported)", GeneratedProjectFiles.Count, OtherProjectFiles.Count );
					}
					else
					{
						WriteProjectFileManifest();
					}
				}
			}
		}

		/// <summary>
		/// Clean project files
		/// </summary>
		/// <param name="InMasterProjectRelativePath">The MasterProjectRelativePath</param>
		/// <param name="InMasterProjectName">The name of the master project</param>
		/// <param name="InIntermediateProjectFilesPath">The intermediate path of project files</param>
		public abstract void CleanProjectFiles(string InMasterProjectRelativePath, string InMasterProjectName, string InIntermediateProjectFilesPath);

		/// <summary>
		/// Configures project generator based on command-line options
		/// </summary>
		/// <param name="Arguments">Arguments passed into the program</param>
		/// <param name="IncludeAllPlatforms">True if all platforms should be included</param>
		protected virtual void ConfigureProjectFileGeneration( String[] Arguments, ref bool IncludeAllPlatforms )
		{
			string ExtraArgument = null;
			foreach( var CurArgument in Arguments )
			{
				if( CurArgument.StartsWith( "-" ) )
				{
					if (CurArgument.StartsWith( "-Platforms=", StringComparison.InvariantCultureIgnoreCase ))
					{
						// Parse the list... will be in Foo+Bar+New format
						string PlatformList = CurArgument.Substring(11);
						while (PlatformList.Length > 0)
						{
							string PlatformString = PlatformList;
							Int32 PlusIdx = PlatformList.IndexOf("+");
							if (PlusIdx != -1)
							{
								PlatformString = PlatformList.Substring(0, PlusIdx);
								PlatformList = PlatformList.Substring(PlusIdx + 1);
							}
							else
							{
								// We are on the last platform... clear the list to exit the loop
								PlatformList = "";
							}

							// Is the string a valid platform? If so, add it to the list
							UnrealTargetPlatform SpecifiedPlatform = UnrealTargetPlatform.Unknown;
							foreach (UnrealTargetPlatform PlatformParam in Enum.GetValues(typeof(UnrealTargetPlatform)))
							{
								if (PlatformString.Equals(PlatformParam.ToString(), StringComparison.InvariantCultureIgnoreCase))
								{
									SpecifiedPlatform = PlatformParam;
									break;
								}
							}

							if (SpecifiedPlatform != UnrealTargetPlatform.Unknown)
							{
								if (ProjectPlatforms.Contains(SpecifiedPlatform) == false)
								{
									ProjectPlatforms.Add(SpecifiedPlatform);
								}
							}
							else
							{
								Log.TraceWarning("ProjectFiles invalid platform specified: {0}", PlatformString);
							}
						}
					}
					else switch( CurArgument.ToUpperInvariant() )
					{
						case "-ALLPLATFORMS":
							IncludeAllPlatforms = true;
							break;

						case "-CURRENTPLATFORM":
							IncludeAllPlatforms = false;
							break;

						case "-THIRDPARTY":
							bGatherThirdPartySource = true;
							break;
						
						case "-GAME":
							// Generates project files for a single game
							// @todo rocketprojects: Needs documentation
							bGeneratingGameProjectFiles = true;
							break;

						case "-ENGINE":
							// Forces engine modules and targets to be included in game-specific project files
							// @todo rocketprojects: Needs documentation
							bAlwaysIncludeEngineModules = true;
							break;

						case "-NOINTELLISENSE":
							bGenerateIntelliSenseData = false;
							break;

						case "-INTELLISENSE":
							bGenerateIntelliSenseData = true;
							break;

						case "-SHIPPINGCONFIGS":
							bIncludeTestAndShippingConfigs = true;
							break;

						case "-NOSHIPPINGCONFIGS":
							bIncludeTestAndShippingConfigs = false;
							break;

						case "-DUMMYCONFIGS":
							bCreateDummyConfigsForUnsupportedPlatforms = true;
							break;

						case "-NODUMMYCONFIGS":
							bCreateDummyConfigsForUnsupportedPlatforms = false;
							break;

						case "-ALLLANGUAGES":
							bAllDocumentationLanguages = true;
							break;
					}
				}
				else
				{
					if ((CurArgument.EndsWith(".uproject", StringComparison.InvariantCultureIgnoreCase) == false) &&
						(CurArgument.EndsWith(".uplugin", StringComparison.InvariantCultureIgnoreCase) == false))
					{
						ExtraArgument = CurArgument;
					}
				}
			}

			if (UnrealBuildTool.HasUProjectFile())
			{
				bGeneratingRocketProjectFiles = UnrealBuildTool.RunningRocket();
			}


			if( bGeneratingGameProjectFiles )
			{
				GameProjectName = Path.GetFileNameWithoutExtension(UnrealBuildTool.GetUProjectFile());
					if (String.IsNullOrEmpty(GameProjectName))
					{
					throw new BuildException("A valid game project was not found in the specified location (" + UnrealBuildTool.GetUProjectPath() + ")");
					}
			}
			else if( bGeneratingRocketProjectFiles )
			{
				// We expected a project path to be passed in
				if (!UnrealBuildTool.HasUProjectFile())
				{
					throw new BuildException( "When -Rocket is used, you must also specify a path to the Rocket project file on the command-line" );
				}

				// Make sure we can get a valid game name out of this project
				var GameName = Path.GetFileNameWithoutExtension(UnrealBuildTool.GetUProjectFile());
				if( String.IsNullOrEmpty( GameName ) )
				{
					throw new BuildException("A valid Rocket game project was not found in the specified location (" + UnrealBuildTool.GetUProjectPath() + ")");
				}
			}
			else
			{
				// At least one extra argument was specified, but we weren't expected it.  Ignored.
			}
		}


		/// <summary>
		/// Adds all game project files, including target projects and config files
		/// </summary>
		private void AddAllGameProjects(Dictionary<string, ProjectFile> GameProjects, string SupportedPlatformNames, MasterProjectFolder ProjectsFolder)
		{
			foreach( var GameFolderAndProjectFile in GameProjects )
			{
				var GameFolderName = GameFolderAndProjectFile.Key;

				// @todo projectfiles: We have engine localization files, but should we also add GAME localization files?

				string GameProjectDirectory = GameFolderName;
				GameProjectDirectory = GameProjectDirectory.Replace("/", "\\");

				// Game config files
				if( IncludeConfigFiles )
				{
					var GameConfigDirectory = Path.Combine(GameProjectDirectory, "Config");
					if( Directory.Exists( GameConfigDirectory ) )
					{
						var GameProjectFile = GameFolderAndProjectFile.Value;
						var DirectoriesToSearch = new List<string>();
						DirectoriesToSearch.Add( GameConfigDirectory );
						GameProjectFile.AddFilesToProject( SourceFileSearch.FindFiles( DirectoriesToSearch, ExcludeNoRedistFiles:bExcludeNoRedistFiles ), GameFolderName );
					}
				}
			}
		}


		/// Adds all engine localization text files to the specified project
		private void AddEngineLocalizationFiles( ProjectFile EngineProject )
		{
			var EngineLocalizationDirectory = Path.Combine( EngineRelativePath, "Content", "Localization" );
			if( Directory.Exists( EngineLocalizationDirectory ) )
			{
				var DirectoriesToSearch = new List<string>();
				DirectoriesToSearch.Add( EngineLocalizationDirectory );
				EngineProject.AddFilesToProject( SourceFileSearch.FindFiles( DirectoriesToSearch, ExcludeNoRedistFiles:bExcludeNoRedistFiles), EngineRelativePath );
			}
		}


        /// Adds all engine template text files to the specified project
        private void AddEngineTemplateFiles( ProjectFile EngineProject )
        {
            var EngineTemplateDirectory = Path.Combine(EngineRelativePath, "Content", "Editor", "Templates");
            if (Directory.Exists(EngineTemplateDirectory))
            {
				var DirectoriesToSearch = new List<string>();
				DirectoriesToSearch.Add( EngineTemplateDirectory );
				EngineProject.AddFilesToProject( SourceFileSearch.FindFiles( DirectoriesToSearch, ExcludeNoRedistFiles:bExcludeNoRedistFiles), EngineRelativePath );
			}
        }


		/// Adds all engine config files to the specified project
		private void AddEngineConfigFiles( ProjectFile EngineProject )
		{
            var EngineConfigDirectory = Path.Combine(EngineRelativePath, "Config" );
			if( Directory.Exists( EngineConfigDirectory ) )
			{
				var DirectoriesToSearch = new List<string>();
				DirectoriesToSearch.Add( EngineConfigDirectory );

				EngineProject.AddFilesToProject( SourceFileSearch.FindFiles( DirectoriesToSearch, ExcludeNoRedistFiles:bExcludeNoRedistFiles), EngineRelativePath );
			}
		}


		/// <summary>
		/// Finds all module files (filtering by platform)
		/// </summary>
		/// <returns>Filtered list of module files</returns>
		private List<string> DiscoverModules()
		{
			var AllModuleFiles = new List<string>();

			// Locate all modules (*.Build.cs files)
			var FoundModuleFiles = RulesCompiler.FindAllRulesSourceFiles( RulesCompiler.RulesFileType.Module, AdditionalSearchPaths:null );
			foreach( var BuildFileName in FoundModuleFiles )
			{
				var CleanBuildFileName = Utils.CleanDirectorySeparators( BuildFileName );

				bool IncludeThisModule = true;

				// Skip NoRedist files if necessary
				if( bExcludeNoRedistFiles )
				{
					if( CleanBuildFileName.IndexOf( Path.DirectorySeparatorChar + "NoRedist" + Path.DirectorySeparatorChar, StringComparison.InvariantCultureIgnoreCase ) != -1 )
					{
						IncludeThisModule = false;
					}
				}

				if( IncludeThisModule )
				{
					AllModuleFiles.Add( CleanBuildFileName );
				}
			}
			return AllModuleFiles;
		}


		/// <summary>
		/// Finds all target files (filtering by platform)
		/// </summary>
		/// <returns>Filtered list of target files</returns>
		protected List<string> DiscoverTargets()
		{
			var AllTargetFiles = new List<string>();

			// Make a list of all platform name strings that we're *not* including in the project files
			var UnsupportedPlatformNameStrings = Utils.MakeListOfUnsupportedPlatforms( SupportedPlatforms );

			// Locate all targets (*.Target.cs files)
			var FoundTargetFiles = RulesCompiler.FindAllRulesSourceFiles( RulesCompiler.RulesFileType.Target, AdditionalSearchPaths:null );
			foreach( var CurTargetFile in FoundTargetFiles )
			{
				var CleanTargetFileName = Utils.CleanDirectorySeparators( CurTargetFile );

				// Skip targets in unsupported platform directories
				bool IncludeThisTarget = true;
				foreach( var CurPlatformName in UnsupportedPlatformNameStrings )
				{
					if( CleanTargetFileName.IndexOf( Path.DirectorySeparatorChar + CurPlatformName + Path.DirectorySeparatorChar, StringComparison.InvariantCultureIgnoreCase ) != -1 )
					{
						IncludeThisTarget = false;
						break;
					}
				}

				// Skip NoRedist files if necessary
				if( bExcludeNoRedistFiles )
				{
					if( CleanTargetFileName.IndexOf( Path.DirectorySeparatorChar + "NoRedist" + Path.DirectorySeparatorChar, StringComparison.InvariantCultureIgnoreCase ) != -1 )
					{
						IncludeThisTarget = false;
					}
				}

				if( IncludeThisTarget )
				{
					AllTargetFiles.Add( CleanTargetFileName );
				}
			}

			return AllTargetFiles;
		}


	
		/// <summary>
		/// Recursively collapses all sub-folders that are redundant.  Should only be called after we're done adding
		/// files and projects to the master project.
		/// </summary>
		/// <param name="Folder">The folder whose sub-folders we should potentially collapse into</param>
		void EliminateRedundantMasterProjectSubFolders( MasterProjectFolder Folder, string ParentMasterProjectFolderPath )
		{
			// NOTE: This is for diagnostics output only
			var MasterProjectFolderPath = String.IsNullOrEmpty( ParentMasterProjectFolderPath ) ? Folder.FolderName : ( ParentMasterProjectFolderPath + "/" + Folder.FolderName );

			// We can eliminate folders that meet all of these requirements:
			//		1) Have only a single project file in them
			//		2) Have no files in the folder except project files, and no sub-folders
			//		3) The project file matches the folder name
			//
			// Additionally, if KeepSourceSubDirectories==false, we can eliminate directories called "Source".
			//
			// Also, we can kill folders that are completely empty.
			

			foreach( var SubFolder in Folder.SubFolders )
			{
				// Recurse
				EliminateRedundantMasterProjectSubFolders( SubFolder, MasterProjectFolderPath );
			}

			var SubFoldersToAdd = new List<MasterProjectFolder>();
			var SubFoldersToRemove = new List<MasterProjectFolder>();
			foreach( var SubFolder in Folder.SubFolders )
			{
				bool CanCollapseFolder = false;

				// 1)
				if( SubFolder.ChildProjects.Count == 1 )
				{
					// 2)
					if( SubFolder.Files.Count == 0 &&
						SubFolder.SubFolders.Count == 0 )
					{
						// 3)
						if (SubFolder.FolderName.Equals(Utils.GetFilenameWithoutAnyExtensions(SubFolder.ChildProjects[0].ProjectFilePath), StringComparison.InvariantCultureIgnoreCase))
						{
							CanCollapseFolder = true;
						}
					}
				}

				if( !KeepSourceSubDirectories )
				{
					if( SubFolder.FolderName.Equals( "Source", StringComparison.InvariantCultureIgnoreCase ) )
					{
						// Avoid collapsing the Engine's Source directory, since there are so many other solution folders in
						// the parent directory.
						if( !Folder.FolderName.Equals( "Engine", StringComparison.InvariantCultureIgnoreCase ) )
						{
							CanCollapseFolder = true;
						}
					}
				}

				if( SubFolder.ChildProjects.Count == 0 && SubFolder.Files.Count == 0 & SubFolder.SubFolders.Count == 0 )
				{
					// Folder is totally empty
					CanCollapseFolder = true;
				}

				if( CanCollapseFolder )
				{
					// OK, this folder is redundant and can be collapsed away.

					SubFoldersToAdd.AddRange( SubFolder.SubFolders );
					SubFolder.SubFolders.Clear();

					Folder.ChildProjects.AddRange( SubFolder.ChildProjects );
					SubFolder.ChildProjects.Clear();

					Folder.Files.AddRange( SubFolder.Files );
					SubFolder.Files.Clear();

					SubFoldersToRemove.Add( SubFolder );
				}
			}

			foreach( var SubFolderToRemove in SubFoldersToRemove )
			{
				Folder.SubFolders.Remove( SubFolderToRemove );
			}
			Folder.SubFolders.AddRange( SubFoldersToAdd );

			// After everything has been collapsed, do a bit of data validation
			{
				foreach( var CurChildProject in Folder.ChildProjects )
				{
					foreach( var OtherChildProject in Folder.ChildProjects )
					{
						if( CurChildProject != OtherChildProject )
						{
							if (Utils.GetFilenameWithoutAnyExtensions(CurChildProject.ProjectFilePath).Equals(Utils.GetFilenameWithoutAnyExtensions(OtherChildProject.ProjectFilePath), StringComparison.InvariantCultureIgnoreCase))
							{
								throw new BuildException( "Detected collision between two project files with the same path in the same master project folder, " + OtherChildProject.ProjectFilePath + " and " + CurChildProject.ProjectFilePath + " (master project folder: " + MasterProjectFolderPath + ")" );
							}
						}
					}
				}

				foreach( var SubFolder in Folder.SubFolders )
				{
					// If the parent folder already has a child project or file item with the same name as this sub-folder, then
					// that's considered an error (it should never have been allowed to have a folder name that collided
					// with project file names or file items, as that's not supported in Visual Studio.)
					foreach( var CurChildProject in Folder.ChildProjects )
					{
						if (Utils.GetFilenameWithoutAnyExtensions(CurChildProject.ProjectFilePath).Equals(SubFolder.FolderName, StringComparison.InvariantCultureIgnoreCase))
						{
							throw new BuildException( "Detected collision between a master project sub-folder " + SubFolder.FolderName + " and a project within the outer folder " + CurChildProject.ProjectFilePath + " (master project folder: " + MasterProjectFolderPath + ")" );
						}
					}
					foreach( var CurFile in Folder.Files )
					{
						if( Path.GetFileName( CurFile ).Equals( SubFolder.FolderName, StringComparison.InvariantCultureIgnoreCase ) )
						{
							throw new BuildException( "Detected collision between a master project sub-folder " + SubFolder.FolderName + " and a file within the outer folder " + CurFile + " (master project folder: " + MasterProjectFolderPath + ")" );
						}
					}
					foreach( var CurFolder in Folder.SubFolders )
					{
						if( CurFolder != SubFolder )
						{
							if( CurFolder.FolderName.Equals( SubFolder.FolderName, StringComparison.InvariantCultureIgnoreCase ) )
							{
								throw new BuildException( "Detected collision between a master project sub-folder " + SubFolder.FolderName + " and a sibling folder " + CurFolder.FolderName + " (master project folder: " + MasterProjectFolderPath + ")" );
							}
						}
					}
				}
			}
		}


		/// <summary>
		/// Adds UnrealBuildTool to the master project
		/// </summary>
		private void AddUnrealBuildToolProject( MasterProjectFolder ProgramsFolder )
		{
			var ProjectFileName = Utils.MakePathRelativeTo( Path.Combine( Path.Combine( EngineRelativePath, "Source" ), "Programs", "UnrealBuildTool", "UnrealBuildTool.csproj" ), MasterProjectRelativePath );
			var UnrealBuildToolProject = new VCSharpProjectFile( ProjectFileName );

			// Store it off as we need it when generating target projects.
			UBTProject = UnrealBuildToolProject;

			// Add the project
			AddExistingProjectFile(UnrealBuildToolProject, bNeedsAllPlatformAndConfigurations:true, bForceDevelopmentConfiguration:true);

			// Put this in a solution folder
			ProgramsFolder.ChildProjects.Add( UnrealBuildToolProject );
		}

		/// <summary>
		/// Adds a C# project to the master project
		/// </summary>
		/// <param name="ProjectName">Name of project file to add</param>
		/// <returns>ProjectFile if the operation was successful, otherwise null.</returns>
		private ProjectFile AddSimpleCSharpProject(string ProjectName)
		{
			ProjectFile Project = null;

			var ProjectFileName = Path.Combine( EngineRelativePath, "Source", "Programs", ProjectName, Path.GetFileName( ProjectName ) + ".csproj" );
			FileInfo Info = new FileInfo( ProjectFileName );
			if( Info.Exists )
			{
				var FileNameRelativeToMasterProject = Utils.MakePathRelativeTo( ProjectFileName, MasterProjectRelativePath );
				Project = new VCSharpProjectFile(FileNameRelativeToMasterProject);
				AddExistingProjectFile(Project);
			}
			else
			{
				throw new BuildException( ProjectFileName + " doesn't exist!" );
			}

			return Project;
		}

		/// <summary>
		/// Adds a Sandcastle Help File Builder project to the master project
		/// </summary>
		/// <param name="ProjectName">Name of project file to add</param>
		private ProjectFile AddSimpleSHFBProject( string ProjectName )
		{
			// We only need this for non-native projects
			ProjectFile Project = null;

			string ProjectFileName = Path.Combine( EngineRelativePath, "Source", "Programs", ProjectName, Path.GetFileName( ProjectName ) + ".shfbproj" );
			FileInfo Info = new FileInfo( ProjectFileName );
			if( Info.Exists )
			{
				string FileNameRelativeToMasterProject = Utils.MakePathRelativeTo( ProjectFileName, MasterProjectRelativePath );
				Project = new VSHFBProjectFile( FileNameRelativeToMasterProject );
				AddExistingProjectFile(Project);
			}
			else
			{
				throw new BuildException( ProjectFileName + " doesn't exist!" );
			}
			
			return Project;
		}

		/// <summary>
		/// Check the registry for MVC3 project support
		/// </summary>
		/// <param name="RootKey"></param>
		/// <param name="VisualStudioVersion"></param>
		/// <returns></returns>
		private bool CheckRegistryKey( RegistryKey RootKey, string VisualStudioVersion )
		{
			bool bInstalled = false;
			RegistryKey VSSubKey = RootKey.OpenSubKey( "SOFTWARE\\Microsoft\\VisualStudio\\" + VisualStudioVersion + "\\Projects\\{E53F8FEA-EAE0-44A6-8774-FFD645390401}" );
			if( VSSubKey != null )
			{
				bInstalled = true;
				VSSubKey.Close();
			}

			return bInstalled;
		}

		/// <summary>
		/// Check to see if a Visual Studio Extension is installed
		/// </summary>
		/// <param name="VisualStudioFolder"></param>
		/// <param name="VisualStudioVersion"></param>
		/// <param name="Extension"></param>
		/// <returns></returns>
		private bool CheckVisualStudioExtensionPackage( string VisualStudioFolder, string VisualStudioVersion, string Extension )
		{
			DirectoryInfo DirInfo = new DirectoryInfo( Path.Combine( VisualStudioFolder, VisualStudioVersion, "Extensions" ) );
			if( DirInfo.Exists )
			{
				List<FileInfo> PackageDefs = DirInfo.GetFiles( "*.pkgdef", SearchOption.AllDirectories ).ToList();
				List<string> PackageDefNames = PackageDefs.Select( x => x.Name ).ToList();
				if( PackageDefNames.Contains( Extension ) )
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Adds all of the IOS C# projects to the master project
		/// </summary>
		private void AddIOSProjects(MasterProjectFolder Folder)
		{
			string ProjectFolderName = Path.Combine( EngineRelativePath, "Source", "Programs", "IOS" );
			DirectoryInfo ProjectFolderInfo = new DirectoryInfo( ProjectFolderName );
			if( ProjectFolderInfo.Exists )
			{
				Folder.ChildProjects.Add( AddSimpleCSharpProject( "IOS/IPhonePackager" ) );
				Folder.ChildProjects.Add( AddSimpleCSharpProject( "IOS/DeploymentInterface" ) );
				Folder.ChildProjects.Add( AddSimpleCSharpProject( "IOS/DeploymentServer" ) );
				Folder.ChildProjects.Add( AddSimpleCSharpProject( "IOS/MobileDeviceInterface" ) );
			}
		}

		/// <summary>
		/// Adds all of the Android C# projects to the master project
		/// </summary>
		private void AddAndroidProjects(MasterProjectFolder Folder)
		{
		}
		

		/// <summary>
		/// Adds all of the config files for program targets to their project files
		/// </summary>
		private void AddEngineProgramConfigFiles( Dictionary<string, ProjectFile> ProgramProjects )
		{
			if( IncludeConfigFiles )
			{
				foreach( var ProjectFolderAndFile in ProgramProjects )
				{
					var ProgramFolder = ProjectFolderAndFile.Key;
					var ProgramProjectFile = ProjectFolderAndFile.Value;

					var ProgramName = Path.GetFileName( ProgramFolder );

					// @todo projectfiles: The config folder for programs is kind of weird -- you end up going UP a few directories to get to it.  This stuff is not great.
					// @todo projectfiles: Fragile assumption here about Programs always being under /Engine/Programs
					var ProgramDirectory = Path.Combine( EngineRelativePath, "Programs", ProgramName );
					var ProgramConfigDirectory = Path.Combine( ProgramDirectory, "Config" );
					if( Directory.Exists( ProgramConfigDirectory ) )
					{
						var DirectoriesToSearch = new List<string>();
						DirectoriesToSearch.Add( ProgramConfigDirectory );
						ProgramProjectFile.AddFilesToProject( SourceFileSearch.FindFiles( DirectoriesToSearch, ExcludeNoRedistFiles:bExcludeNoRedistFiles), ProgramDirectory );
					}
				}
			}
		}


		/// <summary>
		/// Generates data for IntelliSense (compile definitions, include paths)
		/// </summary>
		/// <param name="Arguments">Incoming command-line arguments to UBT</param>
		/// <param name="TargetFiles">Target files</param>
		/// <return>Whether the process was successful or not</return>
		private bool GenerateIntelliSenseData( String[] Arguments, List<string> TargetFiles )
		{
			var bSuccess = true;
			if( ShouldGenerateIntelliSenseData() && TargetFiles.Count > 0 )
			{
				try
				{
					Console.Write( "Binding IntelliSense data... " );

					int ProgressChars = 0;
					int TargetIndex = 0;
					Utils.DisplayProgress( TargetIndex, TargetFiles.Count - 1, ref ProgressChars );

					for( ; TargetIndex < TargetFiles.Count; ++TargetIndex)
					{
						var CurTarget = TargetFiles[ TargetIndex ];
						var TargetName = Utils.GetFilenameWithoutAnyExtensions( CurTarget );	// Twice, to remove both extensions from *.Target.cs file

						Log.TraceVerbose( "Found target: " + TargetName );

						var ArgumentsCopy = new string[ Arguments.Length + 1 ];
						ArgumentsCopy[ 0 ] = TargetName;
						for( var CurEntryIndex = 0; CurEntryIndex < Arguments.Length; ++CurEntryIndex )
						{
							ArgumentsCopy[ CurEntryIndex + 1 ] = Arguments[ CurEntryIndex ];
						}

						bSuccess = UnrealBuildTool.RunUBT( ArgumentsCopy );
						if( !bSuccess )
						{
							break;
						}

						// Display progress
						Utils.DisplayProgress( TargetIndex, TargetFiles.Count - 1, ref ProgressChars );
					}
				}
				finally
				{
					Console.WriteLine();
				}
			}

			return bSuccess;
		}


		/// <summary>
		/// Selects which platforms and build configurations we want in the project file
		/// </summary>
		/// <param name="IncludeAllPlatforms">True if we should include ALL platforms that are supported on this machine.  Otherwise, only desktop platforms will be included.</param>
		/// <param name="SupportedPlatformNames">Output string for supported platforms, returned as comma-separated values.</param>
		protected virtual void SetupSupportedPlatformsAndConfigurations(bool IncludeAllPlatforms, out string SupportedPlatformNames)
		{
			var SupportedPlatformsString = new StringBuilder();

			System.Array PlatformEnums = Enum.GetValues(typeof(UnrealTargetPlatform));
			foreach (UnrealTargetPlatform Platform in PlatformEnums)
			{
				// project is in the explicit platform list or we include them all, we add the valid desktop platforms as they are required
				bool bInProjectPlatformsList = (ProjectPlatforms.Count > 0) ? (UnrealBuildTool.IsValidDesktopPlatform(Platform) || ProjectPlatforms.Contains(Platform)) : true;

				// project is a desktop platform or we have specified some platforms explicitly
				bool IsRequiredPlatform = (UnrealBuildTool.IsValidDesktopPlatform(Platform) || ProjectPlatforms.Count > 0);

				// Only include desktop platforms unless we were explicitly asked to include all platforms or restricted to a single platform.
				if (bInProjectPlatformsList && (IncludeAllPlatforms || IsRequiredPlatform))
				{
					// If there is a build platform present, add it to the SupportedPlatforms list
					UEBuildPlatform BuildPlatform = UEBuildPlatform.GetBuildPlatform( Platform, true );
					if( BuildPlatform != null )
					{
						if (UnrealBuildTool.IsValidPlatform(Platform))
						{
							SupportedPlatforms.Add(Platform);

							if (SupportedPlatformsString.Length > 0)
							{
								SupportedPlatformsString.Append(", ");
							}
							SupportedPlatformsString.Append(Platform.ToString());
						}
					}
				}
				else
				{
					// We have to unregister any build platforms we aren't supporting.
					// Otherwise, they can add modules that can cause issues when doing tasks
					// like generating IntelliSense.
					UEBuildPlatform.UnregisterBuildPlatform(Platform);
				}
			}

			// Add all configurations
			foreach( UnrealTargetConfiguration CurConfiguration in Enum.GetValues( typeof(UnrealTargetConfiguration) ) )
			{
				if( CurConfiguration != UnrealTargetConfiguration.Unknown )
				{
					if (UnrealBuildTool.IsValidConfiguration(CurConfiguration))
					{
						SupportedConfigurations.Add(CurConfiguration);
					}
				}
			}

			SupportedPlatformNames = SupportedPlatformsString.ToString();
		}

		/// <summary>
		/// Find the game which contains a given input file.
		/// </summary>
		/// <param name="AllGameFolders">All game folders</param>
		/// <param name="File">Full path of the file to search for</param>
		protected UProjectInfo FindGameContainingFile(List<UProjectInfo> AllGames, string File)
		{
			foreach (var Game in AllGames)
			{
				string GameFolderPath = Path.GetFullPath(Game.Folder);
				if (Utils.IsFileUnderDirectory(File, GameFolderPath + Path.DirectorySeparatorChar))
				{
					return Game;
				}
			}
			return null;
		}

		/// <summary>
		/// Finds all modules and code files, given a list of games to process
		/// </summary>
		/// <param name="AllGameFolders">All game folders</param>
		/// <param name="ProgramProjects">All program projects</param>
		/// <param name="AllModuleFiles">List of *.Build.cs files for all engine programs and games</param>
		/// <param name="bGatherThirdPartySource">True to gather source code from third party projects too</param>
		protected void AddProjectsForAllModules( List<UProjectInfo> AllGames, Dictionary<string, ProjectFile> ProgramProjects, List<string> AllModuleFiles, bool bGatherThirdPartySource )
		{
			foreach( var CurModuleFile in AllModuleFiles )
			{
				Log.TraceVerbose("AddProjectsForAllModules " + CurModuleFile);

				// The module's "base directory" is simply the directory where its xxx.Build.cs file is stored.  We'll always
				// harvest source files for this module in this base directory directory and all of its sub-directories.
				var ModuleName = Utils.GetFilenameWithoutAnyExtensions(CurModuleFile);		// Remove both ".cs" and ".Build"

				bool WantProjectFileForModule = true;

				// We'll keep track of whether this is an "engine" or "external" module.  This is determined below while loading module rules.
				bool IsEngineModule = false;
				bool IsThirdPartyModule = false;

				// Check to see if this is an Engine module.  That is, the module is located under the "Engine" folder
				string ModuleFileRelativeToEngineDirectory = Utils.MakePathRelativeTo(CurModuleFile, Path.Combine(EngineRelativePath));
				if (!ModuleFileRelativeToEngineDirectory.StartsWith( ".." ) && !Path.IsPathRooted( ModuleFileRelativeToEngineDirectory ))
				{
					// This is an engine module
					IsEngineModule = true;
				}

				string ModuleFileRelativeToThirdPartyDirectory = Utils.MakePathRelativeTo(CurModuleFile, Path.Combine(EngineRelativePath, "Source", "ThirdParty" ));
				if (!ModuleFileRelativeToThirdPartyDirectory.StartsWith( ".." ) && !Path.IsPathRooted( ModuleFileRelativeToThirdPartyDirectory ))
				{
					// This is a third partymodule
					IsThirdPartyModule = true;
				}

				bool IncludePrivateSourceCode = true;
				if( bGeneratingGameProjectFiles || bGeneratingRocketProjectFiles )
				{
					if( IsEngineModule )
					{
						if( bAlwaysIncludeEngineModules )
						{
							if( bGeneratingRocketProjectFiles )
							{
								// This is an engine module, so strip out private code from the Rocket projects.  Rocket users only need public headers to compile
								IncludePrivateSourceCode = false;
							}
						}
						else
						{
							// We were asked to exclude engine modules from the generated projects
							WantProjectFileForModule = false;
						}
					}
				}

				if( WantProjectFileForModule )
				{
					string ProjectFileNameBase = null;
					string BaseFolder = null;

					string PossibleProgramTargetName = Utils.GetFilenameWithoutAnyExtensions( CurModuleFile );

					// @todo projectfiles: This works fine for now, but is pretty busted.  It assumes only one module per program and that it matches the program target file name. (see TTP 307091)
					if( ProgramProjects != null && ProgramProjects.ContainsKey( PossibleProgramTargetName ) )	// @todo projectfiles: When building (in mem projects), ProgramProjects will be null so we are using the UE4 project instead
					{
						ProjectFileNameBase = PossibleProgramTargetName;
						BaseFolder = Path.GetDirectoryName( CurModuleFile );
					}
					else if( IsEngineModule )
					{
						ProjectFileNameBase = EngineProjectFileNameBase;
						BaseFolder = EngineRelativePath;
					}
					else
					{
						// Figure out which game project this target belongs to
						UProjectInfo ProjectInfo = FindGameContainingFile(AllGames, CurModuleFile);
						if(ProjectInfo == null)
						{
							throw new BuildException( "Found a non-engine module file (" + CurModuleFile + ") that did not exist within any of the known game folders" );
						}
						BaseFolder = ProjectInfo.Folder;
						ProjectFileNameBase = ProjectInfo.GameName;
					}

					// Setup a project file entry for this module's project.  Remember, some projects may host multiple modules!
					string ProjectFileName = Utils.MakePathRelativeTo( Path.Combine( IntermediateProjectFilesPath, ProjectFileNameBase + ProjectFileExtension ), MasterProjectRelativePath );
					bool bProjectAlreadyExisted;
					var ProjectFile = FindOrAddProject( ProjectFileName, IncludeInGeneratedProjects:true, bAlreadyExisted:out bProjectAlreadyExisted );

					// Update our module map
					ModuleToProjectFileMap[ ModuleName ] = ProjectFile;
					ProjectFile.IsGeneratedProject = true;

					// Only search subdirectories for non-external modules.  We don't want to add all of the source and header files
					// for every third-party module, unless we were configured to do so.
					var SearchSubdirectories = !IsThirdPartyModule || bGatherThirdPartySource;

					if( bGatherThirdPartySource )
					{
						Log.TraceInformation( "Searching for third-party source files..." );
					}


					// Find all of the source files (and other files) and add them to the project
					var FoundFiles = SourceFileSearch.FindModuleSourceFiles( CurModuleFile, ExcludeNoRedistFiles: bExcludeNoRedistFiles, SearchSubdirectories:SearchSubdirectories, IncludePrivateSourceCode:IncludePrivateSourceCode );
					ProjectFile.AddFilesToProject( FoundFiles, BaseFolder );


					// Is this module part of a plugin?  If so then we'll make sure to add other plugin-related fiels
					var PluginInfo = Plugins.GetPluginInfoForModule( ModuleName );
					if( PluginInfo != null )
					{
						// NOTE: For plugins with multiple modules, we may attempt to re-add the same plugin files here.  That's OK,
						//  this is handled safely in AddFileToProject()!
						var UPluginFilePath = Path.Combine( PluginInfo.Directory, Path.GetFileName( PluginInfo.Directory ) + ".uplugin" );
						if( File.Exists( UPluginFilePath ) )
						{
							ProjectFile.AddFileToProject( UPluginFilePath, BaseFolder );
						}
						else
						{
							throw new BuildException( "Not expecting to find a plugin module with no corresponding .uplugin file.  File '{0}' doesn't exist", UPluginFilePath );
						}

						// Add plugin "resource" files if we have any
						var PluginResourcesFolder = Path.Combine( PluginInfo.Directory, "Resources" );
						if( Directory.Exists( PluginResourcesFolder ) )
						{
							var DirectoriesToSearch = new List<string>();
							DirectoriesToSearch.Add( PluginResourcesFolder );
							ProjectFile.AddFilesToProject( 
								SourceFileSearch.FindFiles(
									DirectoriesToSearch: DirectoriesToSearch,
									ExcludeNoRedistFiles: bExcludeNoRedistFiles ), 
								BaseFolder );
						}
					}
				}
			}
		}


		/// <summary>
		/// Creates project entries for all known targets (*.Target.cs files)
		/// </summary>
		/// <param name="AllGameFolders">All game folders</param>
		/// <param name="EngineProject">The engine project we created</param>
		/// <param name="GameProjects">Map of game folder name to all of the game projects we created</param>
		/// <param name="ProgramProjects">Map of program names to all of the program projects we created</param>
		/// <param name="TemplateGameProjects">Set of template game projects we found.  These will also be in the GameProjects map</param>
		private void AddProjectsForAllTargets( List<UProjectInfo> AllGames, out ProjectFile EngineProject, out Dictionary<string, ProjectFile> GameProjects, out Dictionary<string, ProjectFile> ProgramProjects, out HashSet<ProjectFile> TemplateGameProjects )
		{
			// As we're creating project files, we'll also keep track of whether we created an "engine" project and return that if we have one
			EngineProject = null;
			GameProjects = new Dictionary<string,ProjectFile>( StringComparer.InvariantCultureIgnoreCase );
			ProgramProjects = new Dictionary<string,ProjectFile>( StringComparer.InvariantCultureIgnoreCase );
			TemplateGameProjects = new HashSet<ProjectFile>();


			// Find all of the target files.  This will filter out any modules or targets that don't
			// belong to platforms we're generating project files for.
			var AllTargetFiles = DiscoverTargets();

			foreach( var TargetFilePath in AllTargetFiles )
			{
				var TargetName = Utils.GetFilenameWithoutAnyExtensions(TargetFilePath);		// Remove both ".cs" and ".Target"

				// Check to see if this is an Engine target.  That is, the target is located under the "Engine" folder
				bool IsEngineTarget = false;
				string TargetFileRelativeToEngineDirectory = Utils.MakePathRelativeTo(TargetFilePath, Path.Combine(EngineRelativePath), AlwaysTreatSourceAsDirectory: false);
				if (!TargetFileRelativeToEngineDirectory.StartsWith( ".." ) && !Path.IsPathRooted( TargetFileRelativeToEngineDirectory ))
				{
					// This is an engine target
					IsEngineTarget = true;
				}

				bool WantProjectFileForTarget = true;
				if( bGeneratingGameProjectFiles || bGeneratingRocketProjectFiles )
				{
					if( IsEngineTarget )
					{
						if( !bAlwaysIncludeEngineModules )
						{
							// We were asked to exclude engine modules from the generated projects
							WantProjectFileForTarget = false;
						}
					}
				}

				if( WantProjectFileForTarget )
				{
					// Create target rules for all of the platforms and configuration combinations that we want to enable support for.
					// Just use Win64 or Mac as we only need to recover the target type and both should be supported for all targets...
					string UnusedTargetFilePath;
                    var TargetRulesObject = RulesCompiler.CreateTargetRules( TargetName, new TargetInfo( Utils.IsRunningOnMono ? UnrealTargetPlatform.Mac : UnrealTargetPlatform.Win64, UnrealTargetConfiguration.Development ), false, out UnusedTargetFilePath );


					bool IsProgramTarget = false;

					string GameFolder = null;
					string ProjectFileNameBase = null;
					if( TargetRulesObject.Type == TargetRules.TargetType.Program )
					{
						IsProgramTarget = true;
						ProjectFileNameBase = TargetName;
					}
					else if( IsEngineTarget )
					{
						ProjectFileNameBase = EngineProjectFileNameBase;
					}
					else
					{
						// Figure out which game project this target belongs to
						UProjectInfo ProjectInfo = FindGameContainingFile(AllGames, TargetFilePath);
						if(ProjectInfo == null)
						{
							throw new BuildException( "Found a non-engine target file (" + TargetFilePath + ") that did not exist within any of the known game folders" );
						}
						GameFolder = ProjectInfo.Folder;
						ProjectFileNameBase = ProjectInfo.GameName;
					}

					// @todo projectfiles: We should move all of the Target.cs files out of sub-folders to clean up the project directories a bit (e.g. GameUncooked folder)

					var ProjectFilePath = Path.Combine( IntermediateProjectFilesPath, ProjectFileNameBase + ProjectFileExtension );

					if (TargetRules.IsGameType(TargetRulesObject.Type) &&
						(TargetRules.IsEditorType(TargetRulesObject.Type) == false))
					{
						// Allow platforms to generate stub projects here...
						UEPlatformProjectGenerator.GenerateGameProjectStubs(
							InGenerator: this, 
							InTargetName: TargetName,
							InTargetFilepath: TargetFilePath, 
							InTargetRules: TargetRulesObject,
							InPlatforms: SupportedPlatforms,
							InConfigurations: SupportedConfigurations);
					}

					ProjectFilePath = Utils.MakePathRelativeTo( ProjectFilePath, MasterProjectRelativePath );

					bool bProjectAlreadyExisted;
					var ProjectFile = FindOrAddProject( ProjectFilePath, IncludeInGeneratedProjects: true, bAlreadyExisted:out bProjectAlreadyExisted );
					ProjectFile.IsGeneratedProject = true;
					ProjectFile.IsStubProject = false;

					bool IsTemplateTarget = false;
					{
						// Check to see if this is a template target.  That is, the target is located under the "Templates" folder
						string TargetFileRelativeToTemplatesDirectory = Utils.MakePathRelativeTo(TargetFilePath, Path.Combine(RootRelativePath, "Templates"));
						if (!TargetFileRelativeToTemplatesDirectory.StartsWith("..") && !Path.IsPathRooted(TargetFileRelativeToTemplatesDirectory))
						{
							IsTemplateTarget = true;
						}
					}
					string BaseFolder = null;
					if( IsProgramTarget )
					{
						ProgramProjects[ TargetName ] = ProjectFile;
						BaseFolder = Path.GetDirectoryName( TargetFilePath );
					}
					else if( IsEngineTarget )
					{
						EngineProject = ProjectFile;
						BaseFolder = EngineRelativePath;
					}
					else
					{
						GameProjects[ GameFolder ] = ProjectFile;
						if( IsTemplateTarget )
						{
							TemplateGameProjects.Add( ProjectFile );
						}
						BaseFolder = GameFolder;

						if( !bProjectAlreadyExisted )
						{
							// Add the .uproject file for this game/template
							var UProjectFilePath = Path.Combine( BaseFolder, ProjectFileNameBase + ".uproject" );
							if( File.Exists( UProjectFilePath ) )
							{
								ProjectFile.AddFileToProject( UProjectFilePath, BaseFolder );
							}
							else
							{
								throw new BuildException( "Not expecting to find a game with no .uproject file.  File '{0}' doesn't exist", UProjectFilePath );
							}
						}

					}

					foreach( var ExistingProjectTarget in ProjectFile.ProjectTargets )
					{
						if( ExistingProjectTarget.TargetRules.ConfigurationName.Equals( TargetRulesObject.ConfigurationName, StringComparison.InvariantCultureIgnoreCase ) )
						{
							throw new BuildException( "Not expecting project {0} to already have a target rules of with configuration name {1} ({2}) while trying to add: {3}", ProjectFilePath, TargetRulesObject.ConfigurationName, ExistingProjectTarget.TargetRules.ToString(), TargetRulesObject.ToString() );
						}

						// Not expecting to have both a game and a program in the same project.  These would alias because we share the project and solution configuration names (just because it makes sense to)
						if( ExistingProjectTarget.TargetRules.Type == TargetRules.TargetType.Game && ExistingProjectTarget.TargetRules.Type == TargetRules.TargetType.Program ||
							ExistingProjectTarget.TargetRules.Type == TargetRules.TargetType.Program && ExistingProjectTarget.TargetRules.Type == TargetRules.TargetType.Game )
						{
							throw new BuildException( "Not expecting project {0} to already have a Game/Program target ({1}) associated with it while trying to add: {2}", ProjectFilePath, ExistingProjectTarget.TargetRules.ToString(), TargetRulesObject.ToString() );
						}
					}

					var ProjectTarget = new ProjectTarget()
						{
							TargetRules = TargetRulesObject,
							TargetFilePath = TargetFilePath
						};

					if( TargetName == "ShaderCompileWorker" )		// @todo projectfiles: Ideally, the target rules file should set this
					{
						ProjectTarget.ForceDevelopmentConfiguration = true;
					}

					ProjectFile.ProjectTargets.Add( ProjectTarget );

					// Make sure the *.Target.cs file is in the project.
					ProjectFile.AddFileToProject( TargetFilePath, BaseFolder );


					// We special case ShaderCompileWorker.  It needs to always be compiled in Development mode.
					Log.TraceVerbose( "Generating target {0} for {1}", TargetRulesObject.Type.ToString(), ProjectFilePath );
				}
			}
		}

		
		/// Adds shader source code to the specified project
		protected void AddEngineShaderSource( ProjectFile EngineProject )
		{
			// Setup a project file entry for this module's project.  Remember, some projects may host multiple modules!
			var ShadersDirectory = Path.Combine( EngineRelativePath, "Shaders" );

			var DirectoriesToSearch = new List<string>();
			DirectoriesToSearch.Add( ShadersDirectory );
			var SubdirectoryNamesToExclude = new List<string>();
			{
                // Don't include binary shaders in the project file.
                SubdirectoryNamesToExclude.Add( "Binaries" );
				// We never want shader intermediate files in our project file
				SubdirectoryNamesToExclude.Add( "PDBDump" );
				SubdirectoryNamesToExclude.Add( "WorkingDirectory" );
			}
			EngineProject.AddFilesToProject( SourceFileSearch.FindFiles(
				DirectoriesToSearch: DirectoriesToSearch,
				ExcludeNoRedistFiles: bExcludeNoRedistFiles,
				SubdirectoryNamesToExclude: SubdirectoryNamesToExclude ), EngineRelativePath );
		}


		/// Adds engine build infrastructure files to the specified project
		protected void AddEngineBuildFiles( ProjectFile EngineProject )
		{
			var BuildDirectory = Path.Combine( EngineRelativePath, "Build" );

			var DirectoriesToSearch = new List<string>();
			DirectoriesToSearch.Add( BuildDirectory );
			var SubdirectoryNamesToExclude = new List<string>();
			{
				// Nothing to exclude, yet!
				// SubdirectoryNamesToExclude.Add( "DirectoryName" );
			}
			EngineProject.AddFilesToProject( SourceFileSearch.FindFiles(
				DirectoriesToSearch: DirectoriesToSearch,
				ExcludeNoRedistFiles: bExcludeNoRedistFiles,
				SubdirectoryNamesToExclude: SubdirectoryNamesToExclude ), EngineRelativePath );
		}


		/// Adds engine documentation to the specified project
		protected void AddEngineDocumentation( ProjectFile EngineProject )
		{
			// NOTE: The project folder added here will actually be collapsed away later if not needed
			var DocumentationProjectDirectory = Path.Combine( EngineRelativePath, "Documentation" );
			var DocumentationSourceDirectory = Path.Combine( EngineRelativePath, "Documentation", "Source" );
			DirectoryInfo DirInfo = new DirectoryInfo( DocumentationProjectDirectory );
			if( DirInfo.Exists && Directory.Exists( DocumentationSourceDirectory ) )
			{
				Log.TraceVerbose( "Adding documentation files..." );

				var DirectoriesToSearch = new List<string>();
				DirectoriesToSearch.Add( DocumentationSourceDirectory );

				var SubdirectoryNamesToExclude = new List<string>();
				{
					// We never want any of the images or attachment files included in our generated project
					SubdirectoryNamesToExclude.Add( "Images" );
					SubdirectoryNamesToExclude.Add( "Attachments" );

					// The API directory is huge, so don't include any of it
					SubdirectoryNamesToExclude.Add("API");

					// Omit Javascript source because it just confuses the Visual Studio IDE
					SubdirectoryNamesToExclude.Add( "Javascript" );
				}

				var DocumentationFiles = SourceFileSearch.FindFiles(
					DirectoriesToSearch: DirectoriesToSearch,
					ExcludeNoRedistFiles: bExcludeNoRedistFiles,
					SubdirectoryNamesToExclude: SubdirectoryNamesToExclude );

				// Filter out non-English documentation files if we were configured to do so
				if( !bAllDocumentationLanguages )
				{
					var FilteredDocumentationFiles = new List<string>();
					foreach( var DocumentationFile in DocumentationFiles )
					{
						bool bPassesFilter = true;
						if( DocumentationFile.EndsWith( ".udn", StringComparison.InvariantCultureIgnoreCase ) )
						{
							var LanguageSuffix = Path.GetExtension( Path.GetFileNameWithoutExtension( DocumentationFile ) );
							if( !String.IsNullOrEmpty( LanguageSuffix ) &&
								!LanguageSuffix.Equals( ".int", StringComparison.InvariantCultureIgnoreCase ) )
							{
								bPassesFilter = false;
							}
						}

						if( bPassesFilter )
						{
							FilteredDocumentationFiles.Add( DocumentationFile );
						}
					}
					DocumentationFiles = FilteredDocumentationFiles;
				}

				EngineProject.AddFilesToProject( DocumentationFiles, EngineRelativePath );
			}
			else
			{
				Log.TraceVerbose("Skipping documentation project... directory not found");
			}
		}


		/// <summary>
		/// Adds a new project file and returns an object that represents that project file (or if the project file is already known, returns that instead.)
		/// </summary>
		/// <param name="FilePath">Full path to the project file</param>
		/// <param name="IncludeInGeneratedProjects">True if this project should be included in the set of generated projects.  Only matters when actually generating project files.</param>
		/// <param name="bAlreadyExisted">True if we already had this project file</param>
		/// <returns>Object that represents this project file in Unreal Build Tool</returns>
		public ProjectFile FindOrAddProject( string FilePath, bool IncludeInGeneratedProjects, out bool bAlreadyExisted )
		{
			if( String.IsNullOrEmpty( FilePath ) )
			{
				throw new BuildException( "Not valid to call FindOrAddProject() with an empty file path!" );
			}

			// Do we already have this project?
			ProjectFile ExistingProjectFile;
			if( ProjectFileMap.TryGetValue( FilePath, out ExistingProjectFile ) )
			{
				bAlreadyExisted = true;
				return ExistingProjectFile;
			}

			// Add a new project file for the specified path
			var NewProjectFile = AllocateProjectFile( FilePath );
			ProjectFileMap[ FilePath ] = NewProjectFile;

			if( IncludeInGeneratedProjects )
			{
				GeneratedProjectFiles.Add( NewProjectFile );
			}

			bAlreadyExisted = false;
			return NewProjectFile;
		}


		/// <summary>
		/// Allocates a generator-specific project file object
		/// </summary>
		/// <param name="InitFilePath">Path to the project file</param>
		/// <returns>The newly allocated project file object</returns>
		protected abstract ProjectFile AllocateProjectFile( string InitFilePath );


		/// <summary>
		/// Allocates a generator-specific master project folder object
		/// </summary>
		/// <param name="InitOwnerProjectFileGenerator">Project file generator that owns this object</param>
		/// <param name="InitFolderName">Name for this folder</param>
		/// <returns>The newly allocated project folder object</returns>
		public abstract MasterProjectFolder AllocateMasterProjectFolder(ProjectFileGenerator OwnerProjectFileGenerator, string FolderName );



		/// <summary>
		/// Returns a list of all the known project files
		/// </summary>
		/// <returns>Project file list</returns>
		public List<ProjectFile> AllProjectFiles
		{
			get
			{
				var CombinedList = new List<ProjectFile>();
				CombinedList.AddRange( GeneratedProjectFiles );
				CombinedList.AddRange( OtherProjectFiles );
				return CombinedList;
			}
		}

	
		/// <summary>
		/// Writes the project files to disk
		/// </summary>
		/// <returns>True if successful</returns>
		protected virtual bool WriteProjectFiles()
		{
			try
			{
				Console.Write( "Writing project files... " );

				var TotalProjectFileCount = GeneratedProjectFiles.Count + 1;	// +1 for the master project file, which we'll save next

				int ProgressChars = 0;
				int ProjectFileIndex = 0;
				Utils.DisplayProgress( ProjectFileIndex, TotalProjectFileCount, ref ProgressChars );

				for( ; ProjectFileIndex < GeneratedProjectFiles.Count; ++ProjectFileIndex )
				{
					var CurProject = GeneratedProjectFiles[ ProjectFileIndex ];
					if( !CurProject.WriteProjectFile(
								InPlatforms: SupportedPlatforms,
								InConfigurations: SupportedConfigurations ) )
					{
						return false;
					}

					Utils.DisplayProgress( ProjectFileIndex, TotalProjectFileCount, ref ProgressChars );
				}

				WriteMasterProjectFile( UBTProject: UBTProject );
				++ProjectFileIndex;

				Utils.DisplayProgress( ProjectFileIndex, TotalProjectFileCount, ref ProgressChars );
			}
			finally
			{
				Console.WriteLine();
			}

			return true;
		}

		/// <summary>
		/// Write the project files manifest
		/// This is used by CIS to verify all files referenced are checked into perforce.
		/// </summary>
		protected virtual bool WriteProjectFileManifest()
		{
			FileManifest Manifest = new FileManifest();
			foreach( var CurProject in GeneratedProjectFiles )
			{
				foreach( var SourceFile in CurProject.SourceFiles )
				{
					Manifest.AddFileName( SourceFile.FilePath );
				}
			}

			string ManifestName = Path.Combine( ProjectFileGenerator.IntermediateProjectFilesPath, "UE4SourceFiles.xml" );
			Utils.WriteClass<FileManifest>( Manifest, ManifestName, "" );
			return true;
		}

		/// <summary>
		/// Writes the master project file (e.g. Visual Studio Solution file)
		/// </summary>
		/// <param name="UBTProject">The UnrealBuildTool project</param>
		/// <returns>True if successful</returns>
		protected abstract bool WriteMasterProjectFile( ProjectFile UBTProject );


		/// <summary>
		/// Writes the specified string content to a file.  Before writing to the file, it loads the existing file (if present) to see if the contents have changed
		/// </summary>
		/// <param name="FileName">File to write</param>
		/// <param name="NewFileContents">File content</param>
		/// <returns>True if the file was saved, or if it didn't need to be overwritten because the content was unchanged</returns>
		public static bool WriteFileIfChanged( string FileName, string NewFileContents )
		{
			// Check to see if the file already exists, and if so, load it up
			string LoadedFileContent = null;
			var FileAlreadyExists = File.Exists( FileName );
			if( FileAlreadyExists )
			{
				try
				{
					LoadedFileContent = File.ReadAllText( FileName );
				}
				catch( Exception )
				{
					Log.TraceInformation( "Error while trying to load existing file {0}.  Ignored.", FileName );
				}
			}


			// Don't bother saving anything out if the new file content is the same as the old file's content
			bool FileNeedsSave = true;
			if( LoadedFileContent != null )
			{
				var bIgnoreProjectFileWhitespaces = true;
				if (ProjectFileComparer.CompareOrdinalIgnoreCase(LoadedFileContent, NewFileContents, bIgnoreProjectFileWhitespaces) == 0)
				{
					// Exact match!
					FileNeedsSave = false;
				}

				if( !FileNeedsSave )
				{
					Log.TraceVerbose( "Skipped saving {0} because contents haven't changed.", Path.GetFileName( FileName ) );
				}
			}

			if( FileNeedsSave )
			{
				// Save the file
				try
				{
					Directory.CreateDirectory( Path.GetDirectoryName( FileName ) );
					File.WriteAllText( FileName, NewFileContents, Encoding.UTF8 );
					Log.TraceVerbose( "Saved {0}", Path.GetFileName( FileName ) );
				}
				catch( Exception ex )
				{
					// Unable to write to the project file.
					string Message = string.Format("Error while trying to write file {0}.  The file is probably read-only.", FileName);
					Console.WriteLine();
					Log.TraceError(Message);
					throw new BuildException(ex, Message);
				}
			}

			return true;
		}

		/// <summary>
		/// Adds the given project to the OtherProjects list
		/// </summary>
		/// <param name="InProject">The project to add</param>
		/// <returns>True if successful</returns>
		public void AddExistingProjectFile(ProjectFile InProject, bool bNeedsAllPlatformAndConfigurations = false, bool bForceDevelopmentConfiguration = false, bool bProjectDeploys = false, List<UnrealTargetPlatform> InSupportedPlatforms = null, List<UnrealTargetConfiguration> InSupportedConfigurations = null)
		{
			if( InProject.ProjectTargets.Count != 0 )
			{
				throw new BuildException( "Expecting existing project to not have any ProjectTargets defined yet." );
			}
			
			var ProjectTarget = new ProjectTarget();
			if( bForceDevelopmentConfiguration )
			{
				ProjectTarget.ForceDevelopmentConfiguration = true;
			}
			ProjectTarget.ProjectDeploys = bProjectDeploys;

			if (bNeedsAllPlatformAndConfigurations)
			{
				// Add all platforms
				var AllPlatforms = Enum.GetValues(typeof(UnrealTargetPlatform));
				foreach (UnrealTargetPlatform CurPlatfrom in AllPlatforms)
				{
					ProjectTarget.ExtraSupportedPlatforms.Add(CurPlatfrom);
				}

				// Add all configurations
				var AllConfigurations = Enum.GetValues(typeof(UnrealTargetConfiguration));
				foreach (UnrealTargetConfiguration CurConfiguration in AllConfigurations)
				{
					ProjectTarget.ExtraSupportedConfigurations.Add( CurConfiguration );
				}
			}
			else if (InSupportedPlatforms != null || InSupportedConfigurations != null)
			{
				if (InSupportedPlatforms != null)
				{
					// Add all explicitly specified platforms
					foreach (UnrealTargetPlatform CurPlatfrom in InSupportedPlatforms)
					{
						ProjectTarget.ExtraSupportedPlatforms.Add(CurPlatfrom);
					}
				}
				else
				{
					// Otherwise, add all platforms
					var AllPlatforms = Enum.GetValues(typeof(UnrealTargetPlatform));
					foreach (UnrealTargetPlatform CurPlatfrom in AllPlatforms)
					{
						ProjectTarget.ExtraSupportedPlatforms.Add(CurPlatfrom);
					}
				}

				if (InSupportedConfigurations != null)
				{
					// Add all explicitly specified configurations
					foreach (UnrealTargetConfiguration CurConfiguration in InSupportedConfigurations)
					{
						ProjectTarget.ExtraSupportedConfigurations.Add(CurConfiguration);
					}
				}
				else
				{
					// Otherwise, add all configurations
					var AllConfigurations = Enum.GetValues(typeof(UnrealTargetConfiguration));
					foreach (UnrealTargetConfiguration CurConfiguration in AllConfigurations)
					{
						ProjectTarget.ExtraSupportedConfigurations.Add(CurConfiguration);
					}
				}
			}
			else
			{
				// For existing project files, just support the default desktop platforms and configurations
				UnrealBuildTool.GetAllDesktopPlatforms(ref ProjectTarget.ExtraSupportedPlatforms, false);
				// Debug and Development only
				ProjectTarget.ExtraSupportedConfigurations.Add(UnrealTargetConfiguration.Debug);
				ProjectTarget.ExtraSupportedConfigurations.Add(UnrealTargetConfiguration.Development);
			}

			InProject.ProjectTargets.Add( ProjectTarget );

			// Existing projects must always have a GUID.  This will throw an exception if one isn't found.
			InProject.LoadGUIDFromExistingProject();

			OtherProjectFiles.Add( InProject );
		}


		/// The project for UnrealBuildTool.  Note that when generating Rocket project files, we won't have
		/// an UnrealBuildTool project at all.
		protected ProjectFile UBTProject;

		/// List of platforms that we'll support in the project files
		protected List<UnrealTargetPlatform> SupportedPlatforms = new List<UnrealTargetPlatform>();

		/// List of build configurations that we'll support in the project files
		protected List<UnrealTargetConfiguration> SupportedConfigurations = new List<UnrealTargetConfiguration>();

		/// Map of project file names to their project files.  This includes every single project file in memory or otherwise that
		/// we know about so far.  Note that when generating project files, this map may even include project files that we won't
		/// be including in the generated projects.
		protected readonly Dictionary<string, ProjectFile> ProjectFileMap = new Dictionary<string, ProjectFile>( StringComparer.InvariantCultureIgnoreCase );

		/// List of project files that we'll be generating
		protected readonly List<ProjectFile> GeneratedProjectFiles = new List<ProjectFile>();

		/// List of other project files that we want to include in a generated solution file, even though we
		/// aren't generating them ourselves.  Note that these may *not* always be C++ project files (e.g. C#)
		protected readonly List<ProjectFile> OtherProjectFiles = new List<ProjectFile>();

		/// List of top-level folders in the master project file
		protected MasterProjectFolder RootFolder;
	}

	/// <summary>
	/// Helper class used for comparing the existing and generated project files.
	/// </summary>
	class ProjectFileComparer
	{
		//static readonly string GUIDRegexPattern = "(\\{){0,1}[0-9a-fA-F]{8}\\-[0-9a-fA-F]{4}\\-[0-9a-fA-F]{4}\\-[0-9a-fA-F]{4}\\-[0-9a-fA-F]{12}(\\}){0,1}";
		//static readonly string GUIDReplaceString = "GUID";

		/// <summary>
		/// Used by CompareOrdinalIgnoreWhitespaceAndCase to determine if a whitespace can be ignored.
		/// </summary>
		/// <param name="Whitespace">Whitespace character.</param>
		/// <returns>true if the character can be ignored, false otherwise.</returns>
		static bool CanIgnoreWhitespace(char Whitespace)
		{
			// Only ignore spaces and tabs.
			return Whitespace == ' ' || Whitespace == '\t';
		}

		/*
		/// <summary>
		/// Replaces all GUIDs in the project file with "GUID" text.
		/// </summary>
		/// <param name="ProjectFileContents">Contents of the project file to remove GUIDs from.</param>
		/// <returns>String with all GUIDs replaced with "GUID" text.</returns>
		static string StripGUIDs(string ProjectFileContents)
		{
			// Replace all GUIDs with "GUID" text.
			return System.Text.RegularExpressions.Regex.Replace(ProjectFileContents, GUIDRegexPattern, GUIDReplaceString);
		}
		*/

		/// <summary>
		/// Compares two project files ignoring whitespaces, case and GUIDs.
		/// </summary>
		/// <remarks>
		/// Compares two specified String objects by evaluating the numeric values of the corresponding Char objects in each string.
		/// Only space and tabulation characters are ignored. Ignores leading whitespaces at the beginning of each line and 
		/// differences in whitespace sequences between matching non-whitespace sub-strings.
		/// </remarks>
		/// <param name="StrA">The first string to compare.</param>
		/// <param name="StrB">The second string to compare. </param>
		/// <returns>An integer that indicates the lexical relationship between the two comparands.</returns>
		public static int CompareOrdinalIgnoreWhitespaceAndCase(string StrA, string StrB)
		{
			// Remove GUIDs before processing the strings.
			//StrA = StripGUIDs(StrA);
			//StrB = StripGUIDs(StrB);

			int IndexA = 0;
			int IndexB = 0;
			while (IndexA < StrA.Length && IndexB < StrB.Length)
			{
				char A = Char.ToLowerInvariant(StrA[IndexA]);
				char B = Char.ToLowerInvariant(StrB[IndexB]);
				if (Char.IsWhiteSpace(A) && Char.IsWhiteSpace(B) && CanIgnoreWhitespace(A) && CanIgnoreWhitespace(B))
				{
					// Skip whitespaces in both strings
					for (IndexA++; IndexA < StrA.Length && Char.IsWhiteSpace(StrA[IndexA]) == true; IndexA++) ;
					for (IndexB++; IndexB < StrB.Length && Char.IsWhiteSpace(StrB[IndexB]) == true; IndexB++) ;
				}
				else if (Char.IsWhiteSpace(A) && IndexA > 0 && StrA[IndexA - 1] == '\n')
				{
					// Skip whitespaces in StrA at the beginning of each line
					for (IndexA++; IndexA < StrA.Length && Char.IsWhiteSpace(StrA[IndexA]) == true; IndexA++) ;
				}
				else if (Char.IsWhiteSpace(B) && IndexB > 0 && StrB[IndexB - 1] == '\n')
				{
					// Skip whitespaces in StrA at the beginning of each line
					for (IndexB++; IndexB < StrB.Length && Char.IsWhiteSpace(StrB[IndexB]) == true; IndexB++) ;
				}
				else if (A != B)
				{
					return A - B;
				}
				else
				{
					IndexA++;
					IndexB++;
				}
			}
			// Check if we reached the end in both strings
			return (StrA.Length - IndexA) - (StrB.Length - IndexB);
		}

		/// <summary>
		/// Compares two project files ignoring case and GUIDs.
		/// </summary>
		/// <param name="StrA">The first string to compare.</param>
		/// <param name="StrB">The second string to compare. </param>
		/// <returns>An integer that indicates the lexical relationship between the two comparands.</returns>
		public static int CompareOrdinalIgnoreCase(string StrA, string StrB)
		{
			// Remove GUIDs before processing the strings.
			//StrA = StripGUIDs(StrA);
			//StrB = StripGUIDs(StrB);

			// Use simple ordinal comparison.
			return String.Compare(StrA, StrB, StringComparison.InvariantCultureIgnoreCase);
		}

		/// <summary>
		/// Compares two project files ignoring case and GUIDs.
		/// </summary>
		/// <see cref="CompareOrdinalIgnoreWhitespaceAndCase"/>
		/// <param name="StrA">The first string to compare.</param>
		/// <param name="StrB">The second string to compare. </param>
		/// <param name="bIgnoreWhitespace">True if whitsapces should be ignored.</param>
		/// <returns>An integer that indicates the lexical relationship between the two comparands.</returns>
		public static int CompareOrdinalIgnoreCase(string StrA, string StrB, bool bIgnoreWhitespace)
		{
			if (bIgnoreWhitespace)
			{
				return CompareOrdinalIgnoreWhitespaceAndCase(StrA, StrB);
			}
			else
			{
				return CompareOrdinalIgnoreCase(StrA, StrB);
			}
		}
	}
}