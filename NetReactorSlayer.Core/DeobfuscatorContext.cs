﻿/*
    Copyright (C) 2021 CodeStrikers.org
    This file is part of NetReactorSlayer.
    NetReactorSlayer is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.
    NetReactorSlayer is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.
    You should have received a copy of the GNU General Public License
    along with NetReactorSlayer.  If not, see <http://www.gnu.org/licenses/>.
*/
using dnlib.DotNet;
using dnlib.DotNet.Writer;
using dnlib.PE;
using NETReactorSlayer.Core.Deobfuscators;
using NETReactorSlayer.Core.Helper.De4dot;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace NETReactorSlayer.Core
{
    public class DeobfuscatorContext
    {
        public bool Parse(string[] args)
        {
            #region Parse Arguments
            bool isValid = false;
            string path = string.Empty;
            DeobfuscatorOptions = new DeobfuscatorOptions();
            for (int i = 0; i < args.Length; i++)
            {
                if (File.Exists(args[i]) && !isValid)
                {
                    isValid = true;
                    path = args[i];
                }
                else
                {
                    if (DeobfuscatorOptions.Arguments.Contains(args[i]) && bool.TryParse(args[i + 1], out bool value))
                    {
                        string key = args[i];
                        if (key == "--keep-stack")
                        {
                            KeepOldMaxStack = value;
                            continue;
                        }
                        else if (key == "--preserve-all")
                        {
                            PreserveAll = value;
                            continue;
                        }
                        else if (key == "--no-pause")
                        {
                            NoPause = value;
                            continue;
                        }
                        if (key.StartsWith("--"))
                            key = key.Substring(2, key.Length - 2);
                        else if (key.StartsWith("-"))
                            key = key.Substring(1, key.Length - 1);
                        else
                            continue;
                        if (DeobfuscatorOptions.Dictionary.TryGetValue(key, out IDeobfuscator deobfuscator))
                        {
                            if (value)
                                DeobfuscatorOptions.Stages.Add(deobfuscator);
                            else
                                DeobfuscatorOptions.Stages.Remove(deobfuscator);
                        }
                    }
                }
            }
            #endregion
            if (isValid)
            {
                #region Get Assembly Infos
                SourcePath = path;
                SourceFileName = Path.GetFileNameWithoutExtension(path);
                SourceFileExt = Path.GetExtension(path);
                SourceDir = Path.GetDirectoryName(path);
                DestDir = SourceDir + "\\" + SourceFileName + "_Slayed" + SourceFileExt;
                DestFileName = SourceFileName + "_Slayed" + SourceFileExt;
                ModuleContext = GetModuleContext();
                AssemblyModule = new AssemblyModule(SourcePath, ModuleContext);
                #endregion
                #region Load Assembly
                try
                {
                    Module = AssemblyModule.Load();
                    PEImage = new MyPEImage(DeobUtils.ReadModule(Module));
                    try { Assembly = Assembly.Load(SourcePath); } catch { Assembly = Assembly.UnsafeLoadFrom(SourcePath); }
                    return true;
                }
                catch (Exception ex)
                {
                    try
                    {
                        byte[] unpacked = new NativeUnpacker(new PEImage(SourcePath)).Unpack();
                        if (unpacked != null)
                        {
                            #region Create A Temporary File
                            SourcePath = $"{SourceDir}\\PEImage.tmp";
                            File.WriteAllBytes(SourcePath, unpacked);
                            #endregion
                            AssemblyModule = new AssemblyModule(SourcePath, ModuleContext);
                            Module = AssemblyModule.Load(unpacked);
                            try { Assembly = Assembly.Load(SourcePath); } catch { Assembly = Assembly.UnsafeLoadFrom(SourcePath); }
                            PEImage = new MyPEImage(unpacked);
                            IsNative = true;
                            Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, $"--delete-native-image {Process.GetCurrentProcess().Id} \"{SourcePath}\"") { WindowStyle = ProcessWindowStyle.Hidden });
                            Logger.Done("Native image unpacked.");
                            return true;
                        }
                        else
                        {
                            Logger.Error("Failed to load assembly. " + ex.Message);
                            return false;
                        }
                    }
                    catch (Exception ex1)
                    {
                        Logger.Error("Failed to load assembly. " + ex1.Message);
                        return false;
                    }
                }
                #endregion
            }
            else
            {
                Logger.Error("No input files specified.\r\n");
                Logger.PrintUsage();
                return false;
            }
        }

        ModuleContext GetModuleContext()
        {
            ModuleContext moduleContext = new ModuleContext();
            AssemblyResolver assemblyResolver = new AssemblyResolver(moduleContext);
            Resolver resolver = new Resolver(assemblyResolver);
            moduleContext.AssemblyResolver = assemblyResolver;
            moduleContext.Resolver = resolver;
            assemblyResolver.DefaultModuleContext = moduleContext;
            return moduleContext;
        }

        public void Save()
        {
            try
            {
                if (Module.IsILOnly)
                {
                    ModuleWriterOptions options = new ModuleWriterOptions(Module) { Logger = DummyLogger.NoThrowInstance };
                    if (PreserveAll)
                        options.MetadataOptions.Flags = MetadataFlags.PreserveAll;
                    if (KeepOldMaxStack)
                        options.MetadataOptions.Flags |= MetadataFlags.KeepOldMaxStack;
                    Module.Write(DestDir, options);
                }
                else
                {
                    NativeModuleWriterOptions options = new NativeModuleWriterOptions(Module, false) { Logger = DummyLogger.NoThrowInstance };
                    if (PreserveAll)
                        options.MetadataOptions.Flags = MetadataFlags.PreserveAll;
                    if (KeepOldMaxStack)
                        options.MetadataOptions.Flags |= MetadataFlags.KeepOldMaxStack;
                    Module.NativeWrite(DestDir, options);
                }
                try {
                    if (Module != null)
                        Module.Dispose();
                    Module.Dispose();
                    if (PEImage != null)
                        PEImage.Dispose();
                } catch { }
                Logger.Done("Saved to: " + DestFileName);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save file. " + ex.Message);
            }
        }

        private bool PreserveAll, KeepOldMaxStack;

        public bool NoPause = false;

        public static bool IsNative = false;
        public static string SourceFileName { get; set; }
        public static string SourceFileExt { get; set; }
        public static string SourceDir { get; set; }
        public static string SourcePath { get; set; }
        public static string DestDir { get; set; }
        public static string DestFileName { get; set; }
        public static ModuleDefMD Module { get; set; }
        public static Assembly Assembly { get; set; }
        public static AssemblyModule AssemblyModule { get; set; }
        public static ModuleContext ModuleContext { get; set; }
        public static MyPEImage PEImage { get; set; }
        public DeobfuscatorOptions DeobfuscatorOptions { get; set; }
    }
}