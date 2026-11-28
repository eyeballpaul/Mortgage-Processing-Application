// This deliberately empty template is the safe starting point for the
// preview/apply workflow, which permits only new resource declarations.
targetScope = 'resourceGroup'

// The deployment workflow supplies the reviewed main-branch commit SHA.
@minLength(40)
@maxLength(40)
param releaseCommit string

// Add only new resources here; use existing declarations for shared resources.
// Also update the full-create template and delete allowlist before deploying.
// Returning the commit lets the deployment record which source revision created
// the resources; it is metadata only and has no effect on existing services.
output reviewedCommit string = releaseCommit
