try {
	
	
	
	$bin_path = Split-Path -Path $args[0] -Parent
	$proj_name = Split-Path -Path $args[0] -Leaf
	
	Write-Host "Deploying from [$bin_path] project [$proj_name]"
	
	$extra_dep_names = $args[1..($args.Length-1)]
	
	$dep_path = Join-Path -Path (Split-Path -Path $MyInvocation.MyCommand.Path -Parent) -ChildPath "0Deployed"
	Write-Host "Deploying to [$dep_path]"
	New-Item -Path $dep_path -ItemType Directory -Force
	
	function Rename-FileToOld {
		param (
			[Parameter(Mandatory=$true)]
			[string]$filename
		)
		
		$newFilename = $filename + ".old"
		
		if (Test-Path $newFilename) {
			Rename-FileToOld -filename $newFilename
		}
		
		Rename-Item -Path $filename -NewName $newFilename
	}
	
	Function Is-Invalid ([string] $fileName) {
		
		if ($fileName -like "$proj_name*") { return $false }
		
		foreach ($extra_dep_name in $extra_dep_names) {
			if ($fileName -like "$extra_dep_name*") { return $false }
		}
		
		return $true
	}
	
	foreach ($file in Get-ChildItem -Path $dep_path -File)
	{
		if ($file.Name -like "*.old") { continue }
		if (Is-Invalid $file.Name) { continue }
		
		Rename-FileToOld -filename $file.FullName
		
	}
	
	foreach ($file in Get-ChildItem -Path $bin_path -File)
	{
		if (Is-Invalid $file.Name) {
			Write-Host "Skipped deploying [$file]"
			continue
		}
		
		$p1 = $file.FullName
		$p2 = Join-Path -Path $dep_path -ChildPath $file.Name
		Write-Host "[$p1] => [$p2]"
		Copy-Item -Path $p1 -Destination $p2
		
	}
	
	Remove-Item "$dep_path\*.old" -ErrorAction Ignore
	
	
	
}
catch {
	Write-Host "An error occurred:"
	Write-Host $_
	#pause
	exit 1
}
#pause