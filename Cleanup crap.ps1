Write-Host "Connecting to Azure..." -Foreground Cyan -Background Black
Import-Module Az.Resources 
$azContext= Get-AzContext
if($null -ne $azContext) {
	$azContext = Connect-AzAccount
}
if($null -ne $azContext) {
	
	return
}

Write-Host "* Connected to Azure." -Foreground Cyan -Background Black
$global:SubscriptionId = (Get-AzSubscription).Id
$global:ServicePrincipals=@{}
	

Function AzRemoveMediaServiceAndStorageAndResouceGroupInLocation(
	[String]$StorageName = "regexvideo040519",
	[String]$StorageType = "Standard_LRS",
	[String]$ResourceGroupName = "regexvideo001",
	[String]$Location = "northeurope",
	[String]$MediaServiceName = "regexvideomediaservice1",
	[System.Collections.Hashtable]$Tags = @{"tag1" = "RegExVideo"; "tag2" = "regex.video"})
	{
		$a = Get-AzResourceGroup -Name $ResourceGroupName -Location $Location -Tag $Tags
		if($null -eq $a){
			Write-Host "resouce group", $ResourceGroupName, "not found"
			return
		}
		Remove-AzMediaService -Force -AccountName $MediaServiceName -ResourceGroupName $a.ResourceGroupName -Confirm

		Remove-AzStorageAccount -Force -ResourceGroup $a.ResourceGroupName -Name $StorageName -Confirm

		Remove-AzResourceGroup -Force $a.ResourceGroupName -Confirm
	}


	AzRemoveMediaServiceAndStorageAndResouceGroupInLocation -ResourceGroupName "ResourceGroup001"
	AzRemoveMediaServiceAndStorageAndResouceGroupInLocation 
