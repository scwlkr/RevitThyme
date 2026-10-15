# The installer owns one PATH entry, never an executable outside an immutable bundle.
function ConvertTo-CommandDirectory([string]$Value) {
    if (-not $Value) { return '' }
    $expanded = [Environment]::ExpandEnvironmentVariables($Value.Trim().Trim('"'))
    if (-not [IO.Path]::IsPathRooted($expanded)) { return '' }
    try { return [IO.Path]::GetFullPath($expanded).TrimEnd('\') } catch { return '' }
}
function Test-CommandDirectory([string]$Left, [string]$Right) {
    return [string]::Equals((ConvertTo-CommandDirectory $Left),(ConvertTo-CommandDirectory $Right),[StringComparison]::OrdinalIgnoreCase)
}
function Read-CommandRegistryPath([Microsoft.Win32.RegistryKey]$Key) {
    if (-not $Key -or @($Key.GetValueNames()) -notcontains 'Path') { return @{value='';kind='ExpandString';exists=$false} }
    $kind=$Key.GetValueKind('Path').ToString()
    if ($kind -notin @('String','ExpandString')) { throw 'User PATH has an unsupported registry type. Preserve it.' }
    return @{value=[string]$Key.GetValue('Path','',[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames);kind=$kind;exists=$true}
}
function Write-CommandRegistryPath([Microsoft.Win32.RegistryKey]$Key, $Snapshot) {
    if ($Snapshot.exists) { $Key.SetValue('Path',$Snapshot.value,[Enum]::Parse([Microsoft.Win32.RegistryValueKind],$Snapshot.kind)) }
    else { $Key.DeleteValue('Path',$false) }
}
function Get-CommandPathSnapshot([string]$IsolatedRoot) {
    if ($IsolatedRoot) {
        $path = Assert-NativeHostPath (Join-Path $IsolatedRoot 'user-path.txt')
        if (Test-Path -LiteralPath $path) { return @{value=[IO.File]::ReadAllText($path);kind='ExpandString';exists=$true} }
        return @{value='';kind='ExpandString';exists=$false}
    }
    $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment')
    try { return Read-CommandRegistryPath $key } finally { if($key){$key.Dispose()} }
}
function Get-CommandUserPath([string]$IsolatedRoot) { return (Get-CommandPathSnapshot $IsolatedRoot).value }
function Set-CommandPathSnapshot($Snapshot, [string]$IsolatedRoot) {
    if ($IsolatedRoot) {
        $path=Assert-NativeHostPath (Join-Path $IsolatedRoot 'user-path.txt')
        if($Snapshot.exists){[IO.File]::WriteAllText($path,$Snapshot.value)}elseif(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path}
    } else {
        $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Environment')
        try { Write-CommandRegistryPath $key $Snapshot } finally { $key.Dispose() }
    }
}
function Test-CommandPathSnapshot($Left,$Right) {
    return $Left.value -ceq $Right.value -and $Left.kind -ceq $Right.kind -and $Left.exists -eq $Right.exists
}
function Read-CommandState([string]$Install, [string]$PreviousBundle) {
    $path = Assert-NativeHostPath (Join-Path $Install 'command.json')
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    if ((Get-Item -LiteralPath $path).IsReadOnly) { throw 'Global command state is read-only. Preserve it.' }
    $text = [IO.File]::ReadAllText($path)
    try { $state = $text | ConvertFrom-Json } catch { throw 'Global command state is not owned RevitThyme state. Preserve it.' }
    $fields = @('kind','schema','install_root','bundle','command_directory','path_entry','path_added')
    if (((@($state.PSObject.Properties.Name) | Sort-Object) -join '|') -ne (($fields | Sort-Object) -join '|') -or
        $state.kind -cne 'revitthyme-global-command' -or $state.schema -ne 1 -or $state.path_added -isnot [bool] -or
        -not (Test-CommandDirectory $state.install_root $Install)) { throw 'Global command state is not owned RevitThyme state. Preserve it.' }
    $bundle = Assert-NativeHostPath ([string]$state.bundle)
    if ((Split-Path $bundle -Parent) -ne $Install -or (Split-Path $bundle -Leaf) -cnotmatch '^[a-f0-9]{64}$' -or
        -not (Test-CommandDirectory $state.command_directory (Join-Path $bundle 'cli')) -or
        -not (Test-CommandDirectory $state.path_entry $state.command_directory)) { throw 'Global command state belongs to a different installation. Preserve it.' }
    $manifest = Assert-FoundationPackage $bundle
    if ((Get-FileHash -LiteralPath (Join-Path $bundle 'manifest.json')).Hash.ToLowerInvariant() -cne (Split-Path $bundle -Leaf) -or
        -not $manifest.files.PSObject.Properties['cli/rthyme.exe'] -or
        $manifest.files.'cli/rthyme.exe' -cne $manifest.files.'cli/revitthyme.exe') { throw 'Global command state does not identify a verified immutable command.' }
    return @{value=$state;text=$text}
}
function Remove-CommandEntry([Collections.Generic.List[string]]$Entries, [string]$Entry) {
    for ($i=0; $i -lt $Entries.Count; $i++) {
        if ([string]::Equals($Entries[$i],$Entry,[StringComparison]::OrdinalIgnoreCase)) {
            $Entries.RemoveAt($i)
            return $i
        }
    }
    return -1
}
function New-CommandPlan([string]$Install, [string]$Target, $Manifest, [string]$IsolatedRoot, [string]$PreviousBundle) {
    $previous = Read-CommandState $Install $PreviousBundle
    $beforeSnapshot = Get-CommandPathSnapshot $IsolatedRoot
    $before = $beforeSnapshot.value
    $entries = [Collections.Generic.List[string]]::new()
    if ($before) { foreach ($entry in $before.Split(';')) { $entries.Add($entry) } }
    $removed = $null; $removedIndex = -1
    if ($previous -and $previous.value.path_added) {
        $removedIndex = Remove-CommandEntry $entries $previous.value.path_entry
        if ($removedIndex -ge 0) { $removed = [string]$previous.value.path_entry }
    }
    $afterState = $null; $result = $null; $added = $null
    if ($Target -and $Manifest.files.PSObject.Properties['cli/rthyme.exe']) {
        $directory = Join-Path $Target 'cli'
        $entry = $directory; $owned = $true
        $reinstallOwned = $removed -and (Test-CommandDirectory $removed $directory)
        if (-not $reinstallOwned) {
            for ($i=0; $i -lt $entries.Count; $i++) {
                if (Test-CommandDirectory $entries[$i] $directory) { $entry=$entries[$i]; $entries.RemoveAt($i); $owned=$false; break }
            }
        }
        $entries.Insert(0,$entry)
        if ($owned) { $added=$entry }
        $afterState = [ordered]@{kind='revitthyme-global-command';schema=1;install_root=$Install;bundle=$Target;command_directory=$directory;path_entry=$entry;path_added=$owned} | ConvertTo-Json
        $result = @{path=(Join-Path $directory 'rthyme.exe');directory=$directory;path_added=$owned;restart_terminal=$true}
    }
    return @{statePath=(Join-Path $Install 'command.json');beforeState=$(if($previous){$previous.text}else{$null});afterState=$afterState;beforePath=$before;afterPath=($entries -join ';');beforeSnapshot=$beforeSnapshot;afterSnapshot=@{value=($entries -join ';');kind=$beforeSnapshot.kind;exists=($beforeSnapshot.exists -or $entries.Count -gt 0)};isolatedRoot=$IsolatedRoot;result=$result;added=$added;removed=$removed;removedIndex=$removedIndex;pathWritten=$false;stateWritten=$false}
}
function Set-CommandStateText([string]$Path, $Text) {
    if ($null -eq $Text) { if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path }; return }
    $temporary = Join-Path (Split-Path $Path -Parent) ('.command-'+[Guid]::NewGuid().ToString('N')+'.tmp')
    $backup=$temporary+'.bak'
    try {
        [IO.File]::WriteAllText($temporary,$Text)
        if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($temporary,$Path,$backup) }
        else { [IO.File]::Move($temporary,$Path) }
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
        if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup }
    }
}
function Apply-CommandPlan($Plan) {
    $currentState = $(if(Test-Path -LiteralPath $Plan.statePath){[IO.File]::ReadAllText($Plan.statePath)}else{$null})
    if ($currentState -cne $Plan.beforeState -or -not (Test-CommandPathSnapshot (Get-CommandPathSnapshot $Plan.isolatedRoot) $Plan.beforeSnapshot)) { throw 'Global command settings changed during installation. Preserve them and retry.' }
    if (-not (Test-CommandPathSnapshot $Plan.beforeSnapshot $Plan.afterSnapshot)) {
        Set-CommandPathSnapshot $Plan.afterSnapshot $Plan.isolatedRoot
        $Plan.pathWritten=$true
        if (-not (Test-CommandPathSnapshot (Get-CommandPathSnapshot $Plan.isolatedRoot) $Plan.afterSnapshot)) { throw 'Global command PATH readback failed.' }
    }
    if ($Plan.beforeState -cne $Plan.afterState) {
        Set-CommandStateText $Plan.statePath $Plan.afterState
        $Plan.stateWritten=$true
        $readback = $(if(Test-Path -LiteralPath $Plan.statePath){[IO.File]::ReadAllText($Plan.statePath)}else{$null})
        if ($readback -cne $Plan.afterState) { throw 'Global command state readback failed.' }
    }
}
function Undo-CommandPlan($Plan) {
    if ($Plan.pathWritten) {
        $currentSnapshot=Get-CommandPathSnapshot $Plan.isolatedRoot
        $current=$currentSnapshot.value
        if (Test-CommandPathSnapshot $currentSnapshot $Plan.afterSnapshot) { Set-CommandPathSnapshot $Plan.beforeSnapshot $Plan.isolatedRoot }
        else {
            # Reverse only our owned entry changes when another process edited PATH meanwhile.
            $entries = [Collections.Generic.List[string]]::new()
            if ($current) { foreach ($entry in $current.Split(';')) { $entries.Add($entry) } }
            if ($Plan.added) { $null = Remove-CommandEntry $entries $Plan.added }
            if ($Plan.removed -and -not @($entries | Where-Object { Test-CommandDirectory $_ $Plan.removed }).Count) {
                $entries.Insert([Math]::Min($Plan.removedIndex,$entries.Count),$Plan.removed)
            }
            Set-CommandPathSnapshot @{value=($entries -join ';');kind=$currentSnapshot.kind;exists=$true} $Plan.isolatedRoot
        }
    }
    if ($Plan.stateWritten) {
        $currentState = $(if(Test-Path -LiteralPath $Plan.statePath){[IO.File]::ReadAllText($Plan.statePath)}else{$null})
        if ($currentState -ceq $Plan.afterState) { Set-CommandStateText $Plan.statePath $Plan.beforeState }
        else { Write-Warning 'Global command state changed concurrently; its newer contents were preserved.' }
    }
}
