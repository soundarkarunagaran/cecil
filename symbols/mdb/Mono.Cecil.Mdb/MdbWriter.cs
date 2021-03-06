//
// Author:
//   Jb Evain (jbevain@gmail.com)
//
// Copyright (c) 2008 - 2015 Jb Evain
// Copyright (c) 2008 - 2011 Novell, Inc.
//
// Licensed under the MIT/X11 license.
//

using System;
using System.Collections.Generic;
using System.IO;

using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using Mono.CompilerServices.SymbolWriter;

namespace Mono.Cecil.Mdb {

#if !READ_ONLY
	public class MdbWriterProvider : ISymbolWriterProvider {

		public ISymbolWriter GetSymbolWriter (ModuleDefinition module, string fileName)
		{
			return new MdbWriter (module.Mvid, fileName);
		}

		public ISymbolWriter GetSymbolWriter (ModuleDefinition module, Stream symbolStream)
		{
			throw new NotImplementedException ();
		}
	}

	public class MdbWriter : ISymbolWriter {

		readonly Guid mvid;
		readonly MonoSymbolWriter writer;
		readonly Dictionary<string, SourceFile> source_files;

		public MdbWriter (Guid mvid, string assembly)
		{
			this.mvid = mvid;
			this.writer = new MonoSymbolWriter (assembly);
			this.source_files = new Dictionary<string, SourceFile> ();
		}

		SourceFile GetSourceFile (Document document)
		{
			var url = document.Url;

			SourceFile source_file;
			if (source_files.TryGetValue (url, out source_file))
				return source_file;

			var entry = writer.DefineDocument (url);
			var compile_unit = writer.DefineCompilationUnit (entry);

			source_file = new SourceFile (compile_unit, entry);
			source_files.Add (url, source_file);
			return source_file;
		}

		void Populate (Collection<SequencePoint> sequencePoints, int [] offsets,
			int [] startRows, int [] endRows, int [] startCols, int [] endCols, out SourceFile file)
		{
			SourceFile source_file = null;

			for (int i = 0; i < sequencePoints.Count; i++) {
				var sequence_point = sequencePoints [i];
				offsets [i] = sequence_point.Offset;

				if (source_file == null)
					source_file = GetSourceFile (sequence_point.Document);

				startRows [i] = sequence_point.StartLine;
				endRows [i] = sequence_point.EndLine;
				startCols [i] = sequence_point.StartColumn;
				endCols [i] = sequence_point.EndColumn;
			}

			file = source_file;
		}

		public void Write (MethodDebugInformation info)
		{
			var method = new SourceMethod (info.method);

			var sequence_points = info.SequencePoints;
			int count = sequence_points.Count;
			if (count == 0)
				return;

			var offsets = new int [count];
			var start_rows = new int [count];
			var end_rows = new int [count];
			var start_cols = new int [count];
			var end_cols = new int [count];

			SourceFile file;
			Populate (sequence_points, offsets, start_rows, end_rows, start_cols, end_cols, out file);

			var builder = writer.OpenMethod (file.CompilationUnit, 0, method);

			for (int i = 0; i < count; i++) {
				builder.MarkSequencePoint (
					offsets [i],
					file.CompilationUnit.SourceFile,
					start_rows [i],
					start_cols [i],
					end_rows [i],
					end_cols [i],
					false);
			}

			if (info.scope != null)
				WriteScope (info.scope);

			writer.CloseMethod ();
		}

		void WriteScope (ScopeDebugInformation scope)
		{
			if (scope.Start.Offset == scope.End.Offset)
				return;

			writer.OpenScope(scope.Start.Offset);

			WriteScopeVariables (scope);

			if (scope.HasScopes)
				WriteScopes (scope.Scopes);

			writer.CloseScope(scope.End.Offset);
		}

		void WriteScopes (Collection<ScopeDebugInformation> scopes)
		{
			for (int i = 0; i < scopes.Count; i++)
				WriteScope (scopes [i]);
		}

		void WriteScopeVariables (ScopeDebugInformation scope)
		{
			if (!scope.HasVariables)
				return;

			foreach (var variable in scope.variables)
				if (!string.IsNullOrEmpty (variable.Name))
					writer.DefineLocalVariable (variable.Index, variable.Name);
		}

		readonly static byte [] empty_header = new byte [0];

		public bool GetDebugHeader (out ImageDebugDirectory directory, out byte [] header)
		{
			directory = new ImageDebugDirectory ();
			header = empty_header;
			return false;
		}

		void AddVariables (IList<VariableDebugInformation> variables)
		{
			for (int i = 0; i < variables.Count; i++) {
				var variable = variables [i];
				writer.DefineLocalVariable (i, variable.Name);
			}
		}

		public void Dispose ()
		{
			writer.WriteSymbolFile (mvid);
		}

		class SourceFile : ISourceFile {

			readonly CompileUnitEntry compilation_unit;
			readonly SourceFileEntry entry;

			public SourceFileEntry Entry {
				get { return entry; }
			}

			public CompileUnitEntry CompilationUnit {
				get { return compilation_unit; }
			}

			public SourceFile (CompileUnitEntry comp_unit, SourceFileEntry entry)
			{
				this.compilation_unit = comp_unit;
				this.entry = entry;
			}
		}

		class SourceMethod : IMethodDef {

			readonly MethodDefinition method;

			public string Name {
				get { return method.Name; }
			}

			public int Token {
				get { return method.MetadataToken.ToInt32 (); }
			}

			public SourceMethod (MethodDefinition method)
			{
				this.method = method;
			}
		}
	}
#endif
}
