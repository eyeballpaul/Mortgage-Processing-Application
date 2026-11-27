targetScope = 'resourceGroup'

@minLength(40)
@maxLength(40)
param releaseCommit string

// Add only newly created resources here. Reference shared resources as existing.
// Also add them to the full create template and the deletion allowlist.
output reviewedCommit string = releaseCommit
