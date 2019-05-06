
$greenCheck = @{
    Object          = [Char]10003#8730
    ForegroundColor = 'Green'
    NoNewLine       = $true
}
function Write-GreenTick {
    Write-Host ' ' -NoNewline
    Write-Host @greenCheck
    Write-Host
}
Export-ModuleMember -Function 'Write-*'
Export-ModuleMember -Variable 'greenCheck'