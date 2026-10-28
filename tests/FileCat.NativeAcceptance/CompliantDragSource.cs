// Compile with Windows PowerShell 5.1 Add-Type in the disposable VM.
// This source obeys the returned drag effect: it deletes its owned file only for Move.
using System;
using System.IO;
using System.Windows.Forms;

public sealed class CompliantDragSource : Form
{
    private readonly string file;
    private readonly string receipt;
    private readonly Button drag;

    public CompliantDragSource(string file, string receipt)
    {
        this.file = file;
        this.receipt = receipt;
        Text = "Compliant drag source";
        Width = 500;
        Height = 400;
        drag = new Button { Text = "Drag owned file", Width = 300, Height = 110, Left = 80, Top = 100 };
        Controls.Add(drag);
        drag.MouseDown += OnMouseDown;
    }

    private void OnMouseDown(object sender, MouseEventArgs e)
    {
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, new string[] { file });
        DragDropEffects effect = drag.DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move);
        File.WriteAllText(receipt, effect.ToString());
        if (effect == DragDropEffects.Move) File.Delete(file);
    }

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 2 || !File.Exists(args[0])) return 2;
        Application.EnableVisualStyles();
        Application.Run(new CompliantDragSource(args[0], args[1]));
        return 0;
    }
}
