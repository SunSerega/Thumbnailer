try {
	
	
	
	$dashboard_exe_path = $args[0]
	
	$wScriptShell = New-Object -ComObject WScript.Shell
	$shortcut = $wScriptShell.CreateShortcut("C:\Users\$env:USERNAME\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\Thumbnailer.lnk")
	$shortcut.TargetPath = $dashboard_exe_path
	$shortcut.Arguments = "NoWindow"
	$shortcut.Save()
	
	
	
}
catch {
	Write-Host "An error occurred:"
	Write-Host $_
	#pause
	exit 1
}
#pause