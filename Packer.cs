using System.Text.RegularExpressions;

namespace TestParserCore;

public partial class Packer : Form
{
    public Packer()
    {
        InitializeComponent();
    }

    private void pack_Click(object sender, EventArgs e) {
        var p = new EcmaScriptPacker((EcmaScriptPacker.PackerEncoding) Encoding.SelectedItem, fastDecode.Checked, specialChars.Checked);
        tbResult.Text = p.Pack(tbSource.Text).Replace("\n", "\r\n");
        bSave.Enabled = true;
    }

    private void Packer_Load(object sender, EventArgs e) {
        Encoding.Items.Add(EcmaScriptPacker.PackerEncoding.None);
        Encoding.Items.Add(EcmaScriptPacker.PackerEncoding.Numeric);
        Encoding.Items.Add(EcmaScriptPacker.PackerEncoding.Mid);
        Encoding.Items.Add(EcmaScriptPacker.PackerEncoding.Normal);
        Encoding.Items.Add(EcmaScriptPacker.PackerEncoding.HighAscii);
        Encoding.SelectedItem = EcmaScriptPacker.PackerEncoding.Normal;
    }

    private void Encoding_SelectedIndexChanged(object sender, EventArgs e) {
        fastDecode.Enabled = ((EcmaScriptPacker.PackerEncoding)Encoding.SelectedItem != EcmaScriptPacker.PackerEncoding.None);
    }

    private void llPaste_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) {
        tbSource.Text = (string)Clipboard.GetDataObject()?.GetData(typeof(string))!;
    }

    private void llCopy_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) {
        Clipboard.SetDataObject(tbResult.Text, true);
    }

    private void bClear_Click(object sender, EventArgs e) {
        tbResult.Text = tbSource.Text = string.Empty;
        bSave.Enabled = false;
    }

    private void bLoad_Click(object sender, EventArgs e) {
        var r = ofdSource.ShowDialog(this);
        if (r != DialogResult.OK) return;
        var s = ofdSource.OpenFile();
        TextReader rd = new StreamReader(s);
        var content = rd.ReadToEnd();
        rd.Close();
        s.Close();
        var regex = new Regex("([^\r])(\n+)");
        tbSource.Text = regex.Replace(content, changeUnixLineEndings);
    }

    private string changeUnixLineEndings(Match match) {
        return match.Value.Replace("\n", "\r\n");
    }

    private void bSave_Click(object sender, EventArgs e) {
        var r = sfdResult.ShowDialog(this);
        if (r != DialogResult.OK) return;
        var s = sfdResult.OpenFile();
        TextWriter rd = new StreamWriter(s);
        rd.Write(tbResult.Text);
        rd.Close();
        s.Close();
    }
}