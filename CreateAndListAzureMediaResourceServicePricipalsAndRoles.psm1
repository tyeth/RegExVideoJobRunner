
$Password = ""
$Filter = "regex.video"


Import-Module .\GreenTick.psm1
Import-Module Az.Resources 

$global:SubscriptionId =$null
$global:ServicePrincipals = @{ }

function Connect-IfRequired(){
    
    Write-Host "Connecting to Azure..." -Foreground Cyan -Background Black -NoNewline
    $azContext = Get-AzContext -ListAvailable
    if ($null -eq $azContext) {
        Write-Host "X" -Foreground Red
        Write-Host "Loading interactive Azure Login and requesting info..." -NoNewLine -Foreground Cyan
        $azContext = Connect-AzAccount -ErrorAction SilentlyContinue
    }
    if ($null -eq $azContext) {
        Write-Host "X - Failed!" -Foreground Red
        Throw "Not logged in to azure, Get-AzContext failed, try Connect-AzAccount before running script, aborting."
    }
    Write-GreenTick
    Write-Host "* Connected to Azure." -Foreground Cyan -Background Black
    
    $global:SubscriptionId = (Get-AzSubscription).Id
    return $azContext
}

function TakeInsecurePasswordOrGetPassword {
    param (
        [String]$InsecurePassword
    )
    if ($null -ne $InsecurePassword -and $InsecurePassword -ne "" ) { return $InsecurePassword }
    $secret = Read-Host -Prompt 'Enter Keypass' -AsSecureString
    $username = "regex.video"
    return [System.Management.Automation.PSCredential]::new($username, $secret).GetNetworkCredential().Password
}
Function AzListAllServicePricipalsAndRoles(
    [String]$Filter
) {
    Write-Host "Importing Az.Resources"
    Import-Module Az.Resources
    Connect-IfRequired
    # Get all service principals, and for each one, get all the app role assignments, 
    # resolving the app role ID to it's display name. 
	
	
    # ServicePrincipalNames : {https://17fd7dcb-8800-4c6e-83d2-584793ddbdd3, 40b3ab43-7eb1-4c00-afc0-882bf0bdd14b}
    # ApplicationId         : 40b3ab43-7eb1-4c00-afc0-882bf0bdd14b
    # ObjectType            : ServicePrincipal
    # DisplayName           : mediaservices
    # Id                    : 7a93fa23-c5b9-4f1d-8f74-e3da6caeae3a
    # Type                  :
    Write-Host "Listing Service Principals"
    if ($Filter -ne "") { Write-Host "Filter Active: ", $Filter }
    Get-AzADServicePrincipal | ForEach-Object {
        if ($Filter -eq ""){ Write-Host}
        Write-Host "*** Evaluating ", $_.Id -NoNewline
        if ($_.DisplayName -imatch $Filter ) { Write-Host; Write-Host "** Service Prinicpal Details: "; $_ }
        # Build a hash table of the service principal's app roles. The 0-Guid is
        # used in an app role assignment to indicate that the principal is assigned
        # to the default app role (or rather, no app role).
        $appRoles = @{ "$([Guid]::Empty.ToString())" = "(default)" }
        if ($null -ne $_.AppRoles ) { $_.AppRoles | ForEach-Object { $appRoles[$_.Id] = $_.DisplayName } }
	
        # Get the app role assignments for this app, and add a field for the app role name
        if ($_.DisplayName -imatch $Filter ) {
            Write-Host "** Getting Roles"
        }
        $global:ServicePrincipals[$_.ApplicationId] = Get-AzRoleAssignment -ObjectId ($_.Id)
        $global:ServicePrincipals[$_.ApplicationId] | % { $global:roles | Add-Member $_; $_ }
        # if($null -ne $global:ServicePrincipals[$_.ApplicationId] -and ($global:ServicePrincipals[$_.ApplicationId].Count)-eq 1){
        #	  $global:ServicePrincipals[$_.ApplicationId]
        #	}
        #	  $global:ServicePrincipals[$_.ApplicationId] | Select-Object ResourceDisplayName, PrincipalDisplayName,  Id | ForEach-Object {  $_ | Add-Member "AppRoleDisplayName" $appRoles[$_.Id] -Passthru | Write-Host
        if ($_.DisplayName -imatch $Filter ) {
            Write-Host "**-- Done listing roles" -NoNewLine
        }
        #else {
            Write-GreenTick
        #}
		   	 
    }
}




Function AzCreateServicePrincipalAndMediaServicesRole(
    [String]$global:SubscriptionId,
    [String]$ResourceGroupName,
    [String]$MediaAccountId,
    [String]$ApplicationDisplayName = "regex.video",
    [String]$Password) {
	$Password = TakeInsecurePasswordOrGetPassword $Password
    Import-Module Az.Resources
    Connect-IfRequired
    Set-AzContext -SubscriptionId $global:SubscriptionId

    $ServicePrincipal = Get-AzADServicePrincipal -DisplayName $ApplicationDisplayName
    if ($null -eq $ServicePrincipal) {
        $ServicePrincipal = New-AzADServicePrincipal -DisplayName $ApplicationDisplayName
    }
	
    Get-AzADServicePrincipal -ObjectId $ServicePrincipal.Id 
    $NewRole = $null
    $Scope = "/subscriptions/" + $global:SubscriptionId + "/resourceGroups/" + $ResourceGroupName + "/providers/microsoft.media/mediaservices/" + $mediaAccountId

    $Retries = 0; While ($null -eq $NewRole -and $Retries -le 6) {
        # Sleep here for a few seconds to allow the service principal application to become active (usually, it will take only a couple of seconds)
        Start-Sleep 15
        $NewRole = Get-AzRoleAssignment -ServicePrincipalName $ServicePrincipal.ApplicationId -ErrorAction SilentlyContinue
        if ($null -ne $NewRole) {
            break
        }
        New-AzRoleAssignment -RoleDefinitionName Contributor -ServicePrincipalName $ServicePrincipal.ApplicationId -Scope $Scope | Write-Verbose -ErrorAction SilentlyContinue
        $Retries++;
    }
    return $NewRole

}

Function AzCreateResourceGroup(
    [String]$ResourceGroupName,
    [String]$Location,
    [System.Collections.Hashtable]$Tags ) {
	
    Write-Host "Creating", $ResourceGroupName
    # Resource Group
    New-AzResourceGroup -Name $ResourceGroupName -Location $Location -Tags $Tags
}	

Function AzFetchOrCreateMediaServicesResourceAndStorageInLocation(
    [String]$StorageName = "regexvideo040519",
    [String]$StorageType = "Standard_LRS",
    [String]$ResourceGroupName = "regexvideo001",
    [String]$Location = "northeurope",
    [String]$MediaServiceName = "regexvideomediaservice1",
    [System.Collections.Hashtable]$Tags = @{"tag1" = "RegExVideo"; "tag2" = "regex.video" }) {
    #Storage type = Premium_LRS / Standard_LRS / Standard_GRS etc
	Connect-IfRequired
    
    # Resource Group
    if ($null -eq (Get-AzResourceGroup $ResourceGroupName)) {
        AzCreateResourceGroup $ResourceGroupName $Location $Tags
    }

    # Storage
    $StorageAccount = Get-AzStorageAccount -ResourceGroupName $ResourceGroupName -Name $StorageName
    if ($null -eq $StorageAccount) {
        $isNameAvailable = (Get-AzStorageAccountNameAvailability -AccountName $StorageName)
        if ($null -eq $isNameAvailable -or $isNameAvailable.NameAvailable -ne $true ) {
            Write-Host "Unable to use that storage name", $StorageName
        }
        else {
            Write-Host "Creating storage account ", $StorageName
            $StorageAccount = New-AzStorageAccount -ResourceGroupName $ResourceGroupName -Name $StorageName -Location $Location -Type $StorageType
        }
    }

    # Media Service
    $mediaService = Get-AzMediaService -ResourceGroupName $ResourceGroupName -AccountName $MediaServiceName
    if ($null -eq $mediaService) {
        $isNameAvailable = (Get-AzMediaServiceNameAvailability -AccountName $MediaServiceName)
        if ($null -eq $isNameAvailable -or $isNameAvailable.NameAvailable -ne $true) {
            Write-Host "Unable to use that media service name", $MediaServiceName	
        }
        else {
            Write-Host "Creating Media Service", $MediaServiceName
            $mediaService = New-AzMediaService -ResourceGroupName $ResourceGroupName -AccountName $MediaServiceName -Location $Location -StorageAccountId $StorageAccount.Id -Tag $Tags
        }
    }
    return $mediaService
}

Function AzRemoveMediaServiceAndStorageAndResouceGroupInLocation(
    [String]$StorageName = "regexvideo040519",
    [String]$StorageType = "Standard_LRS",
    [String]$ResourceGroupName = "regexvideo001",
    [String]$Location = "northeurope",
    [String]$MediaServiceName = "regexvideomediaservice1",
    [System.Collections.Hashtable]$Tags = @{"tag1" = "RegExVideo"; "tag2" = "regex.video" }) {
    
        Connect-IfRequired
    
        Write-Host "Looking up resource group in", $Location, "before removal:", $ResourceGroupName, " [Tags: ", tags.ToString, " ]"
			
    $a = Get-AzResourceGroup -Name $ResourceGroupName -Location $Location -Tag $Tags
    $a
    if ($null -eq $a) {
        Write-Host "resouce group", $ResourceGroupName, "not found"
        return
    }
    Write-Host "Removing media service", $MediaServiceName
    Remove-AzMediaService -Force -AccountName $MediaServiceName -ResourceGroupName $a.ResourceGroupName
	
    Write-Host "Removing Storage Account", $StorageName
    Remove-AzStorageAccount -Force -ResourceGroup $a.ResourceGroupName -Name $StorageName
	
    Write-Host "Removing Resource Group", $a.ResourceGroupName
    Remove-AzResourceGroup -Force $a.ResourceGroupName
}
#$r = [Microsoft.Azure.Commands.Resources.Models.Authorization.PSRoleAssignment]$null
#$r.RoleDefinitionName

#AzListAllServicePricipalsAndRoles -Filter $Filter
#	AzFetchOrCreateMediaServicesResourceAndStorageInLocation
#AzCreateServicePrincipalAndMediaServicesRole -Password $Password -SubscriptionId $global:SubscriptionId -ResourceGroupName "regexvideo001" -MediaAccountId "regexvideomediaservice1"
#AzRemoveMediaServiceAndStorageAndResouceGroupInLocation 
Export-ModuleMember -Function 'Az*'