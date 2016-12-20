﻿using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Drawing;
using System.IO.MemoryMappedFiles;
using System.Windows.Forms;
using ReClassNET.Core;
using ReClassNET.Debugger;
using ReClassNET.Memory;
using ReClassNET.Plugins;

namespace LoadBinaryPlugin
{
	public class LoadBinaryPluginExt : Plugin, ICoreProcessFunctions
	{
		private object sync = new object();

		private IPluginHost host;

		private string currentFile;

		private Dictionary<IntPtr, MemoryMappedFile> openFiles;

		public override Image Icon => Properties.Resources.icon;

		public override bool Initialize(IPluginHost host)
		{
			Contract.Requires(host != null);

			//System.Diagnostics.Debugger.Launch();

			if (host == null)
			{
				throw new ArgumentNullException(nameof(host));
			}

			this.host = host;

			host.Process.CoreFunctions.RegisterFunctions("Load Binary", this);

			openFiles = new Dictionary<IntPtr, MemoryMappedFile>();

			return true;
		}

		public override void Terminate()
		{
			foreach (var kv in openFiles)
			{
				kv.Value.Dispose();
			}
			openFiles.Clear();

			host = null;
		}

		/// <summary>Gets a <see cref="MemoryMappedFile"/> by its plugin internal identifier.</summary>
		/// <param name="id">The identifier.</param>
		/// <returns>The file or null if the identifier doesn't exist.</returns>
		private MemoryMappedFile GetMappedFileById(IntPtr id)
		{
			MemoryMappedFile file;
			openFiles.TryGetValue(id, out file);
			return file;
		}

		/// <summary>Logs the exception and removes the file.</summary>
		/// <param name="id">The identifier.</param>
		/// <param name="ex">The exception.</param>
		private void LogErrorAndRemoveFile(IntPtr id, Exception ex)
		{
			Contract.Requires(ex != null);

			MemoryMappedFile file;
			if (openFiles.TryGetValue(id, out file))
			{
				file.Dispose();
			}

			openFiles.Remove(id);

			host.Logger.Log(ex);
		}

		/// <summary>Queries if the file is valid.</summary>
		/// <param name="process">The file to check.</param>
		/// <returns>True if the file is valid, false if not.</returns>
		public bool IsProcessValid(IntPtr process)
		{
			lock (sync)
			{
				return GetMappedFileById(process) != null;
			}
		}

		/// <summary>Opens the file.</summary>
		/// <param name="pid">The file id.</param>
		/// <param name="desiredAccess">The desired access. (ignored)</param>
		/// <returns>A plugin internal handle to the file.</returns>
		public IntPtr OpenRemoteProcess(IntPtr id, ProcessAccess desiredAccess)
		{
			lock (sync)
			{
				if (currentFile.GetHashCode() == id.ToInt32())
				{
					try
					{
						var file = MemoryMappedFile.CreateFromFile(currentFile);

						var handle = file.SafeMemoryMappedFileHandle.DangerousGetHandle();

						openFiles.Add(handle, file);

						return handle;
					}
					catch (Exception ex)
					{
						host.Logger.Log(ex);
					}
				}
			}

			return IntPtr.Zero;
		}

		/// <summary>Closes the file.</summary>
		/// <param name="process">The file to close.</param>
		public void CloseRemoteProcess(IntPtr process)
		{
			lock (sync)
			{
				MemoryMappedFile file;
				if (openFiles.TryGetValue(process, out file))
				{
					openFiles.Remove(process);

					file.Dispose();
				}
			}
		}

		/// <summary>Reads memory of the file.</summary>
		/// <param name="process">The process to read from.</param>
		/// <param name="address">The address to read from.</param>
		/// <param name="buffer">[out] The buffer to read into.</param>
		/// <param name="offset">The offset into the buffer.</param>
		/// <param name="size">The size of the memory to read.</param>
		/// <returns>True if it succeeds, false if it fails.</returns>
		public bool ReadRemoteMemory(IntPtr process, IntPtr address, ref byte[] buffer, int offset, int size)
		{
			lock (sync)
			{
				var file = GetMappedFileById(process);
				if (file != null)
				{
					try
					{
						using (var stream = file.CreateViewStream(address.ToInt64(), size))
						{
							stream.Read(buffer, 0, size);

							return true;
						}
					}
					catch (UnauthorizedAccessException)
					{
						// address + size >= file size
					}
					catch (Exception ex)
					{
						LogErrorAndRemoveFile(process, ex);
					}
				}

				return false;
			}
		}

		/// <summary>Not supported.</summary>
		/// <param name="process">The file to write to.</param>
		/// <param name="address">The address to write to.</param>
		/// <param name="buffer">[in] The memory to write.</param>
		/// <param name="offset">The offset into the buffer.</param>
		/// <param name="size">The size of the memory to write.</param>
		/// <returns>True if it succeeds, false if it fails.</returns>
		public bool WriteRemoteMemory(IntPtr process, IntPtr address, ref byte[] buffer, int offset, int size)
		{
			// Not supported.

			return false;
		}

		/// <summary>Opens a file browser dialog and reports the selected file.</summary>
		/// <param name="callbackProcess">The callback which gets called for the selected file.</param>
		public void EnumerateProcesses(Action<Tuple<IntPtr, string>> callbackProcess)
		{
			if (callbackProcess == null)
			{
				return;
			}

			using (var ofd = new OpenFileDialog())
			{
				ofd.Filter = "All|*.*";

				if (ofd.ShowDialog() == DialogResult.OK)
				{
					currentFile = ofd.FileName;

					callbackProcess(Tuple.Create((IntPtr)currentFile.GetHashCode(), currentFile));
				}
			}
		}

		/// <summary>Not supported.</summary>
		/// <param name="process">The process.</param>
		/// <param name="callbackSection">The callback which gets called for every section.</param>
		/// <param name="callbackModule">The callback which gets called for every module.</param>
		public void EnumerateRemoteSectionsAndModules(IntPtr process, Action<Section> callbackSection, Action<Module> callbackModule)
		{
			// Not supported.
		}

		public void ControlRemoteProcess(IntPtr process, ControlRemoteProcessAction action)
		{
			// Not supported.
		}

		public bool AttachDebuggerToProcess(IntPtr id)
		{
			// Not supported.

			return false;
		}

		public void DetachDebuggerFromProcess(IntPtr id)
		{
			// Not supported.
		}

		public bool AwaitDebugEvent(ref DebugEvent evt, int timeoutInMilliseconds)
		{
			// Not supported.

			return false;
		}

		public void HandleDebugEvent(ref DebugEvent evt)
		{
			// Not supported.
		}

		public bool SetHardwareBreakpoint(IntPtr id, IntPtr address, HardwareBreakpointRegister register, HardwareBreakpointTrigger trigger, HardwareBreakpointSize size, bool set)
		{
			// Not supported.

			return false;
		}
	}
}