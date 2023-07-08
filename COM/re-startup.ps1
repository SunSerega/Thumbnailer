try {
	
	
	
	Start-Sleep -Milliseconds 1000

	$startupFolder = [System.Environment]::GetFolderPath("Startup")
	foreach ($file in (Get-ChildItem -Path $startupFolder -File)) {
		Start-Process -FilePath $file.FullName
	}
	
	
	
}
catch {
	Write-Host "An error occurred:"
	Write-Host $_
	pause
	exit
}
#pause