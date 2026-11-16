using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Siemens.Engineering;

namespace TiaOpennessMcp.Tia
{
    public partial class TiaManager
    {
        public ProjectCreationResult CreateProject(string destinationLocation, string projectName)
        {
            if (_tiaPortal == null)
            {
                throw new InvalidOperationException(
                    "A connected TIA Portal instance is required. Connect to the intended instance before creating a project.");
            }

            if (string.IsNullOrWhiteSpace(destinationLocation))
            {
                throw new ArgumentException("An explicit destination location is required.", nameof(destinationLocation));
            }

            if (string.IsNullOrWhiteSpace(projectName))
            {
                throw new ArgumentException("A project name is required.", nameof(projectName));
            }

            var invalidNameCharacters = "<>:\"/\\|?*";
            if (projectName != projectName.Trim() ||
                projectName == "." ||
                projectName == ".." ||
                projectName.EndsWith(".", StringComparison.Ordinal) ||
                projectName.EndsWith(" ", StringComparison.Ordinal) ||
                projectName.EndsWith(".ap21", StringComparison.OrdinalIgnoreCase) ||
                IsReservedProjectName(projectName) ||
                projectName.Any(character =>
                    char.IsControl(character) ||
                    invalidNameCharacters.IndexOf(character) >= 0 ||
                    Path.GetInvalidFileNameChars().Contains(character)))
            {
                throw new ArgumentException(
                    "The project name must be a plain name without a path, extension, surrounding whitespace, or invalid file-name characters.",
                    nameof(projectName));
            }

            var location = Path.GetFullPath(destinationLocation);
            if (!Directory.Exists(location))
            {
                throw new DirectoryNotFoundException(
                    $"The destination location must be an existing directory: '{location}'.");
            }

            if (Directory.EnumerateFileSystemEntries(location).Any())
            {
                throw new IOException(
                    $"The destination location must be empty to prevent replacing or colliding with an existing project: '{location}'.");
            }

            try
            {
                Project project = _tiaPortal.Projects.Create(new DirectoryInfo(location), projectName);
                _project = project;
                var projectPath = project.Path?.FullName ?? "";
                if (string.IsNullOrWhiteSpace(projectPath))
                {
                    return new ProjectCreationResult
                    {
                        ProjectName = project.Name,
                        DestinationLocation = location,
                        Partial = true,
                        MutationOutcomeUnknown = true,
                        Message = "TIA Portal returned a project without a path. Creation may have completed; inspect the destination before retrying."
                    };
                }

                projectPath = Path.GetFullPath(projectPath);
                var locationPrefix = location.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                if (!projectPath.StartsWith(locationPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return new ProjectCreationResult
                    {
                        ProjectName = project.Name,
                        ProjectPath = projectPath,
                        DestinationLocation = location,
                        Partial = true,
                        Message = "TIA Portal returned a project path outside the requested destination. The project was not rolled back; inspect both locations before retrying."
                    };
                }

                if (!string.Equals(project.Name, projectName, StringComparison.Ordinal))
                {
                    return new ProjectCreationResult
                    {
                        ProjectName = project.Name,
                        ProjectPath = projectPath,
                        DestinationLocation = location,
                        Partial = true,
                        Message = "TIA Portal created a project with a different name than requested. The project was not rolled back; inspect the returned path before retrying."
                    };
                }

                return new ProjectCreationResult
                {
                    Success = true,
                    ProjectName = project.Name,
                    ProjectPath = projectPath,
                    DestinationLocation = location,
                    Message = "Project created by the connected TIA Portal V21 instance."
                };
            }
            catch (EngineeringException ex)
            {
                return ProjectCreationResult.ApiFailure(projectName, location, ex);
            }
            catch (COMException ex)
            {
                return ProjectCreationResult.ApiFailure(projectName, location, ex);
            }
            catch (IOException ex)
            {
                return ProjectCreationResult.ApiFailure(projectName, location, ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                return ProjectCreationResult.ApiFailure(projectName, location, ex);
            }
        }

        private static bool IsReservedProjectName(string projectName)
        {
            var baseName = projectName.Split('.')[0];
            if (string.Equals(baseName, "CON", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(baseName, "PRN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(baseName, "AUX", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(baseName, "NUL", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return baseName.Length == 4 &&
                (baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                 baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                baseName[3] >= '1' &&
                baseName[3] <= '9';
        }
    }

    public sealed class ProjectCreationResult
    {
        public bool Success { get; set; }
        public bool Partial { get; set; }
        public bool MutationOutcomeUnknown { get; set; }
        public string ProjectName { get; set; } = "";
        public string ProjectPath { get; set; } = "";
        public string DestinationLocation { get; set; } = "";
        public string Message { get; set; } = "";

        public static ProjectCreationResult ApiFailure(string projectName, string location, Exception exception)
        {
            return new ProjectCreationResult
            {
                ProjectName = projectName,
                DestinationLocation = location,
                Partial = true,
                MutationOutcomeUnknown = true,
                Message = $"TIA Portal project creation failed after invocation; side effects are not guaranteed to be absent. Inspect the destination before retrying. {exception.GetType().Name}: {exception.Message}"
            };
        }
    }
}
