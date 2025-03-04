try {
	
	
	
	$current_principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
	$is_admin = $current_principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
	if (-not $is_admin) {
		Write-Host "No rights to clear explorer cache"
		return
	}
	
	
	
	$explorer_paths = @()
	foreach ($w in (New-Object -ComObject Shell.Application).Windows()) {
		$explorer_paths += $w.Document.Folder.Self.Path
	}
	
	
	
	Function Kill-Procs ([string] $procName) {
		
		foreach ($process in (Get-Process -name $procName -ErrorAction SilentlyContinue)) {
			Write-Host "Killed "$process.Name
			Stop-Process -Id $process.Id -Force
		}
		
	}
	
	Kill-Procs "explorer"
	Kill-Procs "rundll32"
	Kill-Procs "7+ Taskbar Tweaker"
	
	
	
	$cache_files = Get-ChildItem -Path "C:\Users\$env:USERNAME\AppData\Local\Microsoft\Windows\Explorer" -Filter "*.db"
	while ($true) {
		if ($cache_files.Count -eq 0) { break }
		Write-Host "Trying to clear cache items: "$cache_files.Count
		$next_cache_files = @()
		foreach ($cache_file in $cache_files) {
			$fullName = $cache_file.FullName
			Write-Host $fullName
			#Start-Process "C:\Program Files (x86)\IObit\IObit Unlocker\IObitUnlocker.exe" -ArgumentList '/Delete "$fullName"'
			try {
				$need_del = Test-Path $fullName -ErrorAction Stop
			} catch {
				$need_del = $false
			}
			if ($need_del) {
				try {
					Remove-Item -Path $cache_file.FullName -Force -ErrorAction Stop
				} catch {
					$next_cache_files += $cache_file
				}
			}
		}
		$cache_file = $next_cache_files
		Start-Sleep -Milliseconds 100
		Kill-Procs "explorer"
		Kill-Procs "rundll32"
	}
	Write-Host "Done clearing cache"
	
	Remove-Item -Path "C:\Users\$env:USERNAME\Desktop\Thumbnailer.info.log" -Force -ErrorAction SilentlyContinue
	Remove-Item -Path "C:\Users\$env:USERNAME\Desktop\Thumbnailer.log" -Force -ErrorAction SilentlyContinue
	Remove-Item -Path "C:\Users\$env:USERNAME\Desktop\Thumbnailer broken files" -Recurse -Force -ErrorAction SilentlyContinue
	
	
	
	Start-Process explorer.exe
	foreach ($explorer_path in $explorer_paths) {
		Write-Host "Restarting folder [$explorer_path]"
		Start-Process explorer.exe -ArgumentList "$explorer_path"
	}
	
	#$startupFolder = [System.Environment]::GetFolderPath("Startup")
	#foreach ($file in (Get-ChildItem -Path $startupFolder -File)) {
	#	Start-Process -FilePath $file.FullName
	#}
	
	# Wait until taskbar exists,
	# otherwith 7+ thing breaks
	Start-Sleep -Milliseconds 10000
	Write-Host "Restarting Taskbar Tweaker"
	Start-Process "C:\Program Files\7+ Taskbar Tweaker\7+ Taskbar Tweaker.exe" -ArgumentList "-hidewnd"
	
	
	
}
catch {
	Write-Host "An error occurred:"
	Write-Host $_
	pause
	exit 1
}
pause