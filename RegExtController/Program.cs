using System;


try
{

	foreach (var arg in args)
	{
		var ind = arg.IndexOf(':');
		if (ind == -1) throw new FormatException(arg);

		var command = arg.Remove(ind);
		var exts = arg.Remove(0, ind+1).Split(';');

		switch (command)
		{
			case "add":
				foreach (var ext in exts)
				{
					var key = Microsoft.Win32.Registry.ClassesRoot.CreateSubKey('.'+ext+@"\ShellEx\{e357fccd-a995-4576-b01f-234630154e96}");
					//Console.WriteLine($"old={key.GetValue(null)??"nil"}");
					key.SetValue(null, "{E7CBDB01-06C9-4C8F-A061-2EDCE8598F99}");
				}
				break;
			case "rem":
				foreach (var ext in exts)
					Microsoft.Win32.Registry.ClassesRoot.DeleteSubKey('.'+ext+@"\ShellEx\{e357fccd-a995-4576-b01f-234630154e96}", false);
				break;
			default: throw new FormatException(arg);
		}

	}

}
catch (Exception e)
{
	System.Windows.MessageBox.Show(e.ToString());
}


