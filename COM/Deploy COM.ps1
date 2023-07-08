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
	
	$com_dll = "$com_dir\COM.comhost.dll"
	Write-Host "COM .dll: $com_dll"
	
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
	
	
	
}
catch {
	Write-Host "An error occurred:"
	Write-Host $_
	#pause
	exit 1
}
#pause