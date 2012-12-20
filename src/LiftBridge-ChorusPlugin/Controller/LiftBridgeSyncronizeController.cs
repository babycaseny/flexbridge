﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using Chorus;
using Chorus.FileTypeHanders.lift;
using Chorus.sync;
using Palaso.Lift;
using SIL.LiftBridge.Infrastructure;
using SIL.LiftBridge.Model;
using SIL.LiftBridge.Properties;
using TriboroughBridge_ChorusPlugin;
using TriboroughBridge_ChorusPlugin.Controller;
using TriboroughBridge_ChorusPlugin.View;
using Utilities = TriboroughBridge_ChorusPlugin.Utilities;

namespace SIL.LiftBridge.Controller
{
	[Export(typeof(IBridgeController))]
	internal sealed class LiftBridgeSyncronizeController : ISyncronizeController
	{
		private ISynchronizeProject _projectSynchronizer;
		private MainBridgeForm _mainBridgeForm;
		private LiftProject CurrentProject { get; set; }

		#region IBridgeController implementation

		public void InitializeController(MainBridgeForm mainForm, Dictionary<string, string> options, ControllerType controllerType)
		{
			// As per the API, -p will be the main FW data file.
			// REVIEW (RandyR): What if it is the DB4o file?
			// REVIEW (RandyR): What is sent if the user is a client of the DB4o server?
			_mainBridgeForm = mainForm;
			_projectSynchronizer = new SynchronizeLiftProject();

			CurrentProject = new LiftProject(Path.GetDirectoryName(options["-p"]));
			var liftPathname = CurrentProject.LiftPathname;
			if (liftPathname == null)
			{
				// The tmp file should be there, as well as the lift-ranges file, since we get here after Flex does its export.
				liftPathname = Path.Combine(CurrentProject.PathToProject, CurrentProject.ProjectName + Utilities.LiftExtension);
				File.WriteAllText(liftPathname, Resources.kEmptyLiftFileXml);
			}
			var tmpFile = Directory.GetFiles(CurrentProject.PathToProject, "*.tmp").FirstOrDefault();
			if (tmpFile != null)
			{
				File.Copy(tmpFile, liftPathname, true);
				File.Delete(tmpFile);
			}

			ChorusSystem = Utilities.InitializeChorusSystem(CurrentProject.PathToProject, options["-u"], LiftFolder.AddLiftFileInfoToFolderConfiguration);
			if (ChorusSystem.Repository.Identifier == null)
			{
				// First do a commit, since the repo is brand new.
				var projectConfig = ChorusSystem.ProjectFolderConfiguration;
				ProjectFolderConfiguration.EnsureCommonPatternsArePresent(projectConfig);
				projectConfig.IncludePatterns.Add("**.ChorusRescuedFile");

				LiftSorter.SortLiftFile(liftPathname);
				LiftSorter.SortLiftRangesFile(liftPathname + "-ranges");

				ChorusSystem.Repository.AddAndCheckinFiles(projectConfig.IncludePatterns, projectConfig.ExcludePatterns, "Initial commit");
			}
			ChorusSystem.EnsureAllNotesRepositoriesLoaded();
		}

		public ChorusSystem ChorusSystem { get; private set; }

		public IEnumerable<ControllerType> SupportedControllerActions
		{
			get { return new List<ControllerType> { ControllerType.SendReceiveLift }; }
		}

		public IEnumerable<BridgeModelType> SupportedModels
		{
			get { return new List<BridgeModelType> { BridgeModelType.Lift }; }
		}

		#endregion

		#region ISyncronizeController implementation

		public void Syncronize()
		{
			ChangesReceived = _projectSynchronizer.SynchronizeProject(_mainBridgeForm, ChorusSystem, CurrentProject.PathToProject, Path.GetFileNameWithoutExtension(CurrentProject.LiftPathname));
		}

		public bool ChangesReceived { get; private set; }

		#endregion

		#region Implementation of IDisposable

		/// <summary>
		/// Finalizer, in case client doesn't dispose it.
		/// Force Dispose(false) if not already called (i.e. m_isDisposed is true)
		/// </summary>
		~LiftBridgeSyncronizeController()
		{
			Dispose(false);
			// The base class finalizer is called automatically.
		}

		/// <summary>
		/// Performs application-defined tasks associated with freeing, releasing,
		/// or resetting unmanaged resources.
		/// </summary>
		/// <filterpriority>2</filterpriority>
		public void Dispose()
		{
			Dispose(true);
			// This object will be cleaned up by the Dispose method.
			// Therefore, you should call GC.SupressFinalize to
			// take this object off the finalization queue
			// and prevent finalization code for this object
			// from executing a second time.
			GC.SuppressFinalize(this);
		}

		private bool IsDisposed { get; set; }

		/// <summary>
		/// Executes in two distinct scenarios.
		///
		/// 1. If disposing is true, the method has been called directly
		/// or indirectly by a user's code via the Dispose method.
		/// Both managed and unmanaged resources can be disposed.
		///
		/// 2. If disposing is false, the method has been called by the
		/// runtime from inside the finalizer and you should not reference (access)
		/// other managed objects, as they already have been garbage collected.
		/// Only unmanaged resources can be disposed.
		/// </summary>
		/// <remarks>
		/// If any exceptions are thrown, that is fine.
		/// If the method is being done in a finalizer, it will be ignored.
		/// If it is thrown by client code calling Dispose,
		/// it needs to be handled by fixing the issue.
		/// </remarks>
		private void Dispose(bool disposing)
		{
			if (IsDisposed)
				return;

			if (disposing)
			{
				if (_mainBridgeForm != null)
					_mainBridgeForm.Dispose();

				if (ChorusSystem != null)
					ChorusSystem.Dispose();
			}
			_mainBridgeForm = null;
			ChorusSystem = null;

			IsDisposed = true;
		}

		#endregion
	}
}