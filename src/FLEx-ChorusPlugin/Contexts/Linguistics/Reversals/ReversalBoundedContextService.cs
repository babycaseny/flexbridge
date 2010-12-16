﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FLEx_ChorusPlugin.Infrastructure;
using FLEx_ChorusPlugin.Infrastructure.DomainServices;

namespace FLEx_ChorusPlugin.Contexts.Linguistics.Reversals
{
	/// <summary>
	/// Read/Write/Delete the Reversal bounded context.
	///
	/// The Reversal Index instances, including all they own, need to then be removed from 'classData',
	/// as that stuff will be stored elsewhere.
	///
	/// Each ReversalIndex instance will be in its own file, along with everything it owns (nested ownership, except the pos list it owns, which goes into its own file).
	/// The pattern is:
	/// Linguistics\Reversals\foo\foo.reversal, where foo.reversal is the Reversal Index file and 'foo' is the WritingSystem property of the ReversalIndex.
	/// Linguistics\Reversals\foo\foo-PartsOfSpeech.list
	///
	/// The output file for each will be:
	/// <reversal>
	///		<ReversalIndex>
	/// 1. The "Entries" element's contents will be relocated after the "ReversalIndex" element.
	/// 2. All other owned stuff will be nested here.
	///		</ReversalIndex>
	///		<ReversalInxEntry>Nested for what they own.</ReversalInxEntry>
	///		...
	///		<ReversalInxEntry>Nested for what they own.</ReversalInxEntry>
	/// </reversal>
	/// </summary>
	internal static class ReversalBoundedContextService
	{
		private const string ReversalRootFolder = "Reversals";

		internal static void NestContext(string linguisticsBaseDir,
			IDictionary<string, SortedDictionary<string, string>> classData,
			Dictionary<string, string> guidToClassMapping)
		{
			var allLexDbs = classData["LexDb"].FirstOrDefault();
			if (allLexDbs.Value == null)
				return; // No LexDb, then there can be no reversals.

			SortedDictionary<string, string> sortedInstanceData = classData["ReversalIndex"];
			if (sortedInstanceData.Count == 0)
				return; // no reversals, as in Lela-Teli-3.

			var lexDb = XElement.Parse(allLexDbs.Value);
			lexDb.Element("ReversalIndexes").RemoveNodes(); // Restored in FlattenContext method.

			var reversalDir = Path.Combine(linguisticsBaseDir, ReversalRootFolder);
			if (!Directory.Exists(reversalDir))
				Directory.CreateDirectory(reversalDir);

			var srcDataCopy = new SortedDictionary<string, string>(sortedInstanceData);
			foreach (var reversalIndexKvp in srcDataCopy)
			{
				var revIndexElement = XElement.Parse(reversalIndexKvp.Value);
				var ws = revIndexElement.Element("WritingSystem").Element("Uni").Value;
				var revIndexDir = Path.Combine(reversalDir, ws);
				if (!Directory.Exists(revIndexDir))
					Directory.CreateDirectory(revIndexDir);

				var reversalFilename = ws + ".reversal";

				// Break out ReversalIndex's PartsOfSpeech(CmPossibilityList OA) and write in its own .list file.
				FileWriterService.WriteNestedListFileIfItExists(
					classData, guidToClassMapping,
					revIndexElement, SharedConstants.PartsOfSpeech,
					Path.Combine(revIndexDir, ws + "-" + SharedConstants.PartsOfSpeechFilename));

				CmObjectNestingService.NestObject(false, revIndexElement,
					classData,
					guidToClassMapping);

				var entriesElement = revIndexElement.Element("Entries");
				var root = new XElement("Reversal",
					new XElement(SharedConstants.Header, revIndexElement));
				if (entriesElement != null && entriesElement.Elements().Any())
				{
					root.Add(entriesElement.Elements());
						// NB: These were already sorted, way up in MultipleFileServices::CacheDataRecord, since "Entries" is a collection prop.
					entriesElement.RemoveNodes();
				}

				FileWriterService.WriteNestedFile(Path.Combine(revIndexDir, reversalFilename), root);
				classData["LexDb"][lexDb.Attribute(SharedConstants.GuidStr).Value.ToLowerInvariant()] = lexDb.ToString();
			}
		}

		internal static void FlattenContext(
			SortedDictionary<string, XElement> highLevelData,
			SortedDictionary<string, XElement> sortedData,
			string linguisticsBaseDir)
		{
			var reversalDir = Path.Combine(linguisticsBaseDir, ReversalRootFolder);
			if (!Directory.Exists(reversalDir))
				return;

			var lexDb = highLevelData["LexDb"];
			var sortedRevs = new SortedDictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
			foreach (var revIndexDirectoryName in Directory.GetDirectories(reversalDir))
			{
				var dirInfo = new DirectoryInfo(revIndexDirectoryName);
				var ws = dirInfo.Name;
				var reversalPathname = Path.Combine(revIndexDirectoryName, ws + "." + SharedConstants.Reversal);
				var reversalDoc = XDocument.Load(reversalPathname);

				// Put entries back into index's Entries element.
				var root = reversalDoc.Element("Reversal");
				var header = root.Element(SharedConstants.Header);
				var revIdxElement = header.Element("ReversalIndex");

				// Restore POS list, if it exists.
				var catPathname = Path.Combine(revIndexDirectoryName, ws + "-" + SharedConstants.PartsOfSpeechFilename);
				if (File.Exists(catPathname))
				{
					var catListDoc = XDocument.Load(catPathname);
					BaseDomainServices.RestoreElement(
						catPathname,
						sortedData,
						revIdxElement, SharedConstants.PartsOfSpeech,
						catListDoc.Root.Element(SharedConstants.CmPossibilityList)); // Owned elment.
				}

				// Put all records back in ReversalIndex, before sort and restore.
				// EXCEPT, if there is only one of them and it is guid.Empty, then skip it
				var sortedRecords = new SortedDictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
				foreach (var recordElement in root.Elements("ReversalIndexEntry")
					.Where(element => element.Attribute(SharedConstants.GuidStr).Value.ToLowerInvariant() != SharedConstants.EmptyGuid))
				{
					// Add it to Records property of revIdxElement, BUT in sorted order, below, and then flatten dnMainElement.
					sortedRecords.Add(recordElement.Attribute(SharedConstants.GuidStr).Value.ToLowerInvariant(), recordElement);
				}

				if (sortedRecords.Count > 0)
				{
					var recordsElementOwningProp = revIdxElement.Element("Entries")
						?? CmObjectFlatteningService.AddNewPropertyElement(revIdxElement, "Entries");

					foreach (var sortedChartElement in sortedRecords.Values)
						recordsElementOwningProp.Add(sortedChartElement);
				}
				CmObjectFlatteningService.FlattenObject(reversalPathname, sortedData, revIdxElement, lexDb.Attribute(SharedConstants.GuidStr).Value.ToLowerInvariant()); // Restore 'ownerguid' to indices.

				var revIdxGuid = revIdxElement.Attribute(SharedConstants.GuidStr).Value.ToLowerInvariant();
				sortedRevs.Add(revIdxGuid, BaseDomainServices.CreateObjSurElement(revIdxGuid));
			}

			// Restore lexDb ReversalIndexes property in sorted order.
			if (sortedRevs.Count == 0)
				return;
			var reversalsOwningProp = lexDb.Element("ReversalIndexes")
									  ?? CmObjectFlatteningService.AddNewPropertyElement(lexDb, "ReversalIndexes");
			foreach (var sortedRev in sortedRevs.Values)
				reversalsOwningProp.Add(sortedRev);
		}
	}
}