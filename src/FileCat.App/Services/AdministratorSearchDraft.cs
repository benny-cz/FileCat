using FileCat.Core.Search;

namespace FileCat.App.Services;

internal sealed record AdministratorSearchDraft(SearchCriteria Criteria, DateTime CapturedAtUtc, string[] IgnoredFolders,
    SearchRetryFolder[] Folders);
