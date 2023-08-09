using System;

using System.Linq;
using System.Collections.Generic;

using System.Windows.Controls;

namespace Dashboard
{
	public partial class AllowedExt : UserControl
	{
		private static readonly FileExtList temp_ext_list = new();

		public static event Action<bool>? Changed;
		public static (string[] add, string[] rem) GetChanges() => (
			temp_ext_list.Where(ext => !Settings.Root.AllowedExts.Contains(ext)).ToArray(),
			Settings.Root.AllowedExts.Where(ext => !temp_ext_list.Contains(ext)).ToArray()
		);
		public static void CommitChanges() =>
			Settings.Root.AllowedExts = new(temp_ext_list);

		public string Ext => tb_name.Text;

		[Obsolete("Only for designer")]
		public AllowedExt() { InitializeComponent(); }

		public AllowedExt(string ext, StackPanel allowed_ext_container)
		{
			InitializeComponent();

			tb_name.Text = ext;
			if (!temp_ext_list.Add(ext))
				throw new InvalidOperationException();

			{
				var i1 = 0;
				var i2 = allowed_ext_container.Children.Count;
				while (i1 != i2)
				{
					var m = (i1+i2) / 2;
					var m_el = (AllowedExt)allowed_ext_container.Children[m];
					if (string.CompareOrdinal(m_el.Ext, ext) > 0)
						i2 = m;
					else
						i1=m+1;
				}
				allowed_ext_container.Children.Insert(i1, this);
			}
			Changed?.Invoke(temp_ext_list != Settings.Root.AllowedExts);

			b_delete.Click += (o, e) => Delete(allowed_ext_container);

		}

		public void Delete(StackPanel allowed_ext_container)
		{
			if (!temp_ext_list.Remove(Ext))
				throw new InvalidOperationException();
			Changed?.Invoke(temp_ext_list != Settings.Root.AllowedExts);
			allowed_ext_container.Children.Remove(this);
		}

	}
}
