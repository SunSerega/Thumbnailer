try {
	
	
	
	foreach ($process in (Get-Process -name explorer)) {
		#if ($process.mainwindowtitle -eq "" ) {
			Write-Host "Killed " + ($process.Name)
			Stop-Process -Id $process.Id -Force
		#}
	}
	
	#foreach ($process in (Get-Process -Name "rundll32")) {
	#	Write-Host "Killed " + ($process.Name)
	#	Stop-Process -Id $process.Id -Force
	#}
	
	foreach ($process in (Get-Process -Name "7+ Taskbar Tweaker")) {
		Write-Host "Killed " + ($process.Name)
		Stop-Process -Id $process.Id -Force
	}
	
	
	
	Start-Sleep -Milliseconds 3000
	Remove-Item -Path "C:\Users\$env:USERNAME\AppData\Local\Microsoft\Windows\Explorer\*.db" -Recurse -Force
	Remove-Item -Path "C:\Users\$env:USERNAME\Desktop\Thumbnailer.info.log" -Force -ErrorAction SilentlyContinue
	Remove-Item -Path "C:\Users\$env:USERNAME\Desktop\Thumbnailer.log" -Force -ErrorAction SilentlyContinue
	Remove-Item -Path "C:\Users\$env:USERNAME\Desktop\Thumbnailer broken files" -Recurse -Force -ErrorAction SilentlyContinue
	
	
	
	Start-Process explorer
	# Wait until taskbar exists,
	# otherwith 7+ thing breaks
	Start-Sleep -Milliseconds 5000
	
	$startupFolder = [System.Environment]::GetFolderPath("Startup")
	foreach ($file in (Get-ChildItem -Path $startupFolder -File)) {
		Start-Process -FilePath $file.FullName
	}
	
	
	
}
catch {
	Write-Host "An error occurred:"
	Write-Host $_
	pause
	exit 1
}
pause