try {
	$dashboard_exe_path = $args[0]
	Write-Host "Restart: $dashboard_exe_path"
	Start-Process "$dashboard_exe_path" -ArgumentList "NoWindow"
}
catch {
	Write-Host "An error occurred:"
	Write-Host $_
	#pause
	exit 1
}
#pause