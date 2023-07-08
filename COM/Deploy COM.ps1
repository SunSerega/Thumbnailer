try {
	
	
	
	$user_name = $env:COMPUTERNAME
	$bin_dir = $args[0].Remove($args[0].Length-1)
	$com_dir = Join-Path -Path $bin_dir -ChildPath "../Deployed"
	
	Write-Host "Binary dir: $bin_dir"
	Write-Host "Deploy dir: $com_dir"
	Write-Host "User name: $user_name"
	
	Remove-Item -Path $com_dir -Recurse -Force -ErrorAction SilentlyContinue
	$counter = 1
	while ($true)
	{
		$test_com_dir = Join-Path -Path $com_dir -ChildPath $counter
		Write-Host "Trying folder: $test_com_dir"
		if (Test-Path $test_com_dir) {
			Write-Host "Already exists"
		} else {
			Write-Host "Using this folder"
			$com_dir = $test_com_dir
			break;
		}
		$counter++
	}
	
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
	
	$com_dll = "$com_dir\COM.comhost.dll"
	Write-Host "COM .dll: $com_dll"
	
	# foreach ($line in (& "g:\0prog\utils\handle\handle.exe")) {
		# if ($line -match '\s+\spid:') {
			# $exe = $line
		# }
		# elseif ($line -eq $com_dll)  {
			# write-host "[lock] $exe - $line"
		# }
	# }
	
	New-Item -Path $com_dir -ItemType Directory
	Copy-Item -Path $bin_dir\* -Destination $com_dir -Recurse -Force
	
	$reg_key = "HKLM:\SOFTWARE\Classes\CLSID\{E7CBDB01-06C9-4C8F-A061-2EDCE8598F99}"
	Remove-Item -Path $reg_key -Recurse
	regsvr32 /s "$com_dll"
	while (-not (Test-Path -Path $reg_key)) {
		Write-Host "Waiting for regsvr32 to create key"
		Start-Sleep -Milliseconds 100
	}
	New-ItemProperty -Path $reg_key -Name "DisableProcessIsolation" -PropertyType DWORD -Value 1
	
	Remove-Item -Path "C:\Users\$env:USERNAME\AppData\Local\Microsoft\Windows\Explorer\*.db" -Recurse -Force
	Remove-Item -Path "C:\Users\$env:USERNAME\Desktop\Thumbnailer.info.log" -Force -ErrorAction SilentlyContinue
	Remove-Item -Path "C:\Users\$env:USERNAME\Desktop\Thumbnailer.log" -Force -ErrorAction SilentlyContinue
	Remove-Item -Path "C:\Users\$env:USERNAME\Desktop\Thumbnailer broken files" -Recurse -Force -ErrorAction SilentlyContinue
	
	
	
}
catch {
	Write-Host "An error occurred:"
	Write-Host $_
	#pause
	exit 1
}
#pause