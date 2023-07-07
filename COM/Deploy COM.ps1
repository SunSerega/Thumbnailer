try {
	
	
	
	$bin_dir = $args[0].Remove($args[0].Length-1)
	$com_dir = Join-Path -Path $bin_dir -ChildPath "../Deployed"
	$user_name = $env:COMPUTERNAME
	
	Write-Host "Binary dir: $bin_dir"
	Write-Host "Deploy dir: $com_dir"
	Write-Host "User name: $user_name"
	
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
	
	Remove-Item -Path $com_dir -Recurse -Force
	New-Item -Path $com_dir -ItemType Directory
	Copy-Item -Path $bin_dir\* -Destination $com_dir -Recurse -Force
	
	regsvr32 /s "$com_dll"
	
	Remove-Item -Path "C:\Users\$env:USERNAME\AppData\Local\Microsoft\Windows\Explorer\*.db" -Recurse -Force
	Remove-Item -Path "C:\Users\$env:USERNAME\Desktop\Thumbnailer.info.log" -Force
	Remove-Item -Path "C:\Users\$env:USERNAME\Desktop\Thumbnailer.log" -Force
	Remove-Item -Path "C:\Users\$env:USERNAME\Desktop\Thumbnailer broken files" -Recurse -Force
	
	
	
}
catch {
	Write-Host "An error occurred:"
	Write-Host $_
	pause
	exit
}
#pause