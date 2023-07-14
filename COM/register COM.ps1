try {
	
	
	
	$comhost_dll_path = $args[0]
	Write-Host "COM .dll: $comhost_dll_path"
	
	$reg_key = "HKLM:\SOFTWARE\Classes\CLSID\{E7CBDB01-06C9-4C8F-A061-2EDCE8598F99}"
	Remove-Item -Path $reg_key -Recurse
	
	regsvr32 /s "$comhost_dll_path"
	while (-not (Test-Path -Path $reg_key)) {
		Write-Host "Waiting for regsvr32 to create key"
		Start-Sleep -Milliseconds 100
	}
	New-ItemProperty -Path $reg_key -Name "DisableProcessIsolation" -PropertyType DWORD -Value 1
	
	
	
}
catch {
	Write-Host "An error occurred:"
	Write-Host $_
	#pause
	exit 1
}
#pause