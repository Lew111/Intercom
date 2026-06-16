#ifndef ABOUTDIALOG_H
#define ABOUTDIALOG_H

#include <QDialog>
#include <QTabWidget>      
#include <QTextBrowser>    
#include <QScrollArea>

class QTabWidget;
class QTextBrowser;
class QLabel;

class AboutDialog : public QDialog
{
    Q_OBJECT

public:
    explicit AboutDialog(QWidget* parent = nullptr);
    ~AboutDialog();

private:
    void setupUI();
    void createInfoTab();
    void createCreditsTab();
    void createLicenseTab();
    void createChangelogTab();

    QTabWidget* m_tabWidget;
    QTextBrowser* m_creditsText;
    QTextBrowser* m_licenseText;
    QTextBrowser* m_changelogText;
};

#endif 